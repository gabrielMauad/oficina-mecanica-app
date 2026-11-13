using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using OficinaMecanica.Contracts.IntegrationEvents;

namespace IntegrationTests.EventBus;

/// <summary>
/// Participação da OS na Saga coreografada: publica <see cref="OrcamentoAprovadoIntegrationEvent"/> ao
/// aprovar o orçamento e reage a <see cref="PagamentoAprovadoIntegrationEvent"/>,
/// <see cref="PagamentoRecusadoIntegrationEvent"/> e <see cref="EstoqueBaixaFalhouIntegrationEvent"/>
/// (publicados aqui no lugar dos serviços Pagamento e PecasInsumos).
/// </summary>
[Collection("Integration")]
public class SagaIntegrationEventTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    public SagaIntegrationEventTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(
        Guid ClienteId, Guid VeiculoId, Guid ServicoId, Guid PecaId, Guid OsId, Guid OrcamentoId);

    /// <summary>Abre uma OS pelo fluxo completo (orçamento Enviado, OrcamentoGerado publicado).</summary>
    private async Task<Cenario> AbrirOsComOrcamentoEnviadoAsync(HttpClient oficina, string rotulo, int quantidadePeca = 2)
    {
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome($"Peça {rotulo}"), preco: 40.00m, estoque: 20, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome($"Cliente {rotulo}"), email: TestData.Email(rotulo.ToLowerInvariant()));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome($"Serviço {rotulo}"), preco: 100.00m);

        var response = await oficina.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = quantidadePeca } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var osId = (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;

        var orcamentoId = await _factory.ObterOrcamentoIdPublicadoAsync(osId);
        return new Cenario(clienteId, veiculoId, servicoId, pecaId, osId, orcamentoId);
    }

    private async Task AprovarOrcamentoAsync(Cenario cenario)
    {
        using var cliente = _factory.CreateClienteAuthenticatedClient(cenario.ClienteId);
        (await cliente.PatchAsync(
            $"/api/v1/ordens-servico/{cenario.OsId}/aprovar-orcamento", EmptyJsonContent()))
            .EnsureSuccessStatusCode();
    }

    private async Task<string> StatusDaOsAsync(HttpClient client, Guid osId)
    {
        using var os = await OficinaMecanicaWebApplicationFactory.ObterOsJsonAsync(client, osId);
        return os.RootElement.GetProperty("status").GetString()!;
    }

    // ── Aprovação publica OrcamentoAprovado ─────────────────────────────────

    [Fact(DisplayName = "Aprovar orçamento → publica OrcamentoAprovado e a OS continua AguardandoAprovacao")]
    public async Task AprovarOrcamento_DevePublicarOrcamentoAprovado()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "Aprovacao", quantidadePeca: 2);
        var harness = _factory.CriarHarness();
        Assert.False(await harness.Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));

        await AprovarOrcamentoAsync(cenario);

        Assert.True(await harness.Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));
        var evento = harness.Published.Select<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId).Single().Context.Message;
        Assert.NotEqual(Guid.Empty, evento.EventId);
        Assert.Equal(cenario.OrcamentoId, evento.OrcamentoId);
        Assert.Equal(cenario.ClienteId, evento.ClienteId);
        Assert.Equal(180m, evento.ValorTotal); // 1 × 100 (serviço) + 2 × 40 (peça)
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    // ── PagamentoAprovado → EmExecucao ──────────────────────────────────────

    [Fact(DisplayName = "Consumir PagamentoAprovado → OS EmExecucao; reentrega do evento não gera erro")]
    public async Task PagamentoAprovado_DeveColocarAOsEmExecucao()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "PagAprovado");
        await AprovarOrcamentoAsync(cenario);
        var harness = _factory.CriarHarness();

        await harness.Bus.Publish(new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId));

        await _factory.AguardarStatusDaOsAsync(oficina, cenario.OsId, "EmExecucao");

        // Reentrega: o consumer termina sem erro e o estado não muda.
        var reentrega = new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId);
        await harness.Bus.Publish(reentrega);
        await AguardarConsumoSemErroAsync(harness, reentrega);
        Assert.Equal("EmExecucao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    [Fact(DisplayName = "PATCH /executar não existe mais: a execução só começa pelo pagamento aprovado")]
    public async Task RotaExecutar_NaoExiste()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "SemExecutar");
        await AprovarOrcamentoAsync(cenario);

        var response = await oficina.PatchAsync(
            $"/api/v1/ordens-servico/{cenario.OsId}/executar", EmptyJsonContent());

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Esperava 404/405, recebeu {(int)response.StatusCode}.");
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    // ── PagamentoRecusado → compensação ─────────────────────────────────────

    [Fact(DisplayName = "Consumir PagamentoRecusado → orçamento Rejeitado e OrcamentoRejeitado publicado com as peças")]
    public async Task PagamentoRecusado_DeveRejeitarOrcamentoEPublicarEstorno()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "PagRecusado", quantidadePeca: 3);
        await AprovarOrcamentoAsync(cenario);
        var harness = _factory.CriarHarness();
        Assert.False(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));

        await harness.Bus.Publish(new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId,
            "cc_rejected_other_reason"));

        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado");
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));
        var evento = harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId).Single().Context.Message;
        Assert.Equal(cenario.OrcamentoId, evento.OrcamentoId);
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(cenario.PecaId, peca.PecaInsumoId);
        Assert.Equal(3, peca.Quantidade);
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    [Fact(DisplayName = "PagamentoRecusado: reentrega não publica um segundo estorno e a OS aceita novo diagnóstico")]
    public async Task PagamentoRecusado_ReentregaNaoDuplicaEstorno_EOsAceitaNovoDiagnostico()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "Reentrega");
        await AprovarOrcamentoAsync(cenario);
        var harness = _factory.CriarHarness();
        var recusado = new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId, "recusado");

        await harness.Bus.Publish(recusado);
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado");
        var reentrega = recusado with { EventId = Guid.NewGuid() };
        await harness.Bus.Publish(reentrega);
        await AguardarConsumoSemErroAsync(harness, reentrega);

        Assert.Single(harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));

        // A OS segue Aguardando Aprovação e aceita um novo diagnóstico (novo orçamento).
        var diagnostico = await oficina.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{cenario.OsId}/registrar-diagnostico", new
            {
                DescricaoDiagnostico = "Novo diagnóstico após recusa do pagamento.",
                Servicos = new[] { new { ServicoId = cenario.ServicoId, Quantidade = 1 } },
                Pecas = new[] { new { PecaInsumoId = cenario.PecaId, Quantidade = 1 } }
            });
        Assert.Equal(HttpStatusCode.OK, diagnostico.StatusCode);
        using var os = await OficinaMecanicaWebApplicationFactory.ObterOsJsonAsync(oficina, cenario.OsId);
        Assert.Equal(2, os.RootElement.GetProperty("orcamentos").GetArrayLength());
    }

    /// <summary>
    /// Novo diagnóstico após a rejeição do primeiro orçamento: gera o segundo orçamento, que o
    /// handler de <c>OrcamentoGerado</c> deixa Enviado. Devolve o id do novo orçamento.
    /// </summary>
    private async Task<Guid> RegistrarNovoDiagnosticoAsync(HttpClient oficina, Cenario cenario)
    {
        var diagnostico = await oficina.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{cenario.OsId}/registrar-diagnostico", new
            {
                DescricaoDiagnostico = "Novo diagnóstico após a rejeição do orçamento.",
                Servicos = new[] { new { ServicoId = cenario.ServicoId, Quantidade = 1 } },
                Pecas = new[] { new { PecaInsumoId = cenario.PecaId, Quantidade = 1 } }
            });
        Assert.Equal(HttpStatusCode.OK, diagnostico.StatusCode);

        var harness = _factory.CriarHarness();
        await OficinaMecanicaWebApplicationFactory.AguardarAsync(
            () => Task.FromResult(harness.Published.Select<OrcamentoGeradoIntegrationEvent>(
                x => x.Context.Message.OrdemServicoId == cenario.OsId).Count() == 2),
            $"segundo OrcamentoGerado da OS {cenario.OsId} publicado");
        var novoOrcamentoId = harness.Published.Select<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId).Last().Context.Message.OrcamentoId;
        Assert.NotEqual(cenario.OrcamentoId, novoOrcamentoId);

        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Enviado", indice: 1);
        return novoOrcamentoId;
    }

    [Fact(DisplayName = "Ciclo de compensação: pagamento recusado → novo diagnóstico → aprovação → pagamento aprovado do novo orçamento → EmExecucao")]
    public async Task CicloDeCompensacao_PagamentoRecusadoEDepoisAprovadoDoNovoOrcamento_ExecutaAOs()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "Ciclo");
        await AprovarOrcamentoAsync(cenario);
        var harness = _factory.CriarHarness();

        // Pagamento do orçamento X recusado: X Rejeitado, estorno publicado, OS segue aguardando.
        await harness.Bus.Publish(new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId, "recusado"));
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado", indice: 0);
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId && x.Context.Message.OrcamentoId == cenario.OrcamentoId));
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));

        // Novo diagnóstico gera o orçamento Y, que o cliente aprova.
        var orcamentoY = await RegistrarNovoDiagnosticoAsync(oficina, cenario);
        await AprovarOrcamentoAsync(cenario);
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Aprovado", indice: 1);
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));

        // Pagamento aprovado do orçamento Y: a OS entra em execução.
        await harness.Bus.Publish(new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, orcamentoY));

        await _factory.AguardarStatusDaOsAsync(oficina, cenario.OsId, "EmExecucao");
    }

    [Fact(DisplayName = "PagamentoAprovado de orçamento Rejeitado (com outro Aprovado na OS): a OS continua AguardandoAprovacao")]
    public async Task PagamentoAprovado_DeOrcamentoRejeitado_NaoExecutaAOs()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "PagOrcRejeitado");
        await AprovarOrcamentoAsync(cenario);
        var harness = _factory.CriarHarness();
        await harness.Bus.Publish(new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId, "recusado"));
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado", indice: 0);
        await RegistrarNovoDiagnosticoAsync(oficina, cenario);
        await AprovarOrcamentoAsync(cenario);
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Aprovado", indice: 1);

        // Pagamento aprovado referente ao orçamento X, já Rejeitado.
        var pagamentoDoOrcamentoRejeitado = new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), cenario.OsId, cenario.OrcamentoId);
        await harness.Bus.Publish(pagamentoDoOrcamentoRejeitado);

        await AguardarConsumoSemErroAsync(harness, pagamentoDoOrcamentoRejeitado);
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    // ── EstoqueBaixaFalhou → Rejeitado sem estorno ──────────────────────────

    [Fact(DisplayName = "Consumir EstoqueBaixaFalhou → orçamento Rejeitado e nenhum OrcamentoRejeitado publicado")]
    public async Task EstoqueBaixaFalhou_DeveRejeitarOrcamentoSemPublicarEstorno()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "EstoqueFalhou");
        var harness = _factory.CriarHarness();

        await harness.Bus.Publish(new EstoqueBaixaFalhouIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, cenario.OsId, cenario.OrcamentoId, "estoque insuficiente"));

        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado");
        Assert.False(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId));
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, cenario.OsId));
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou: reentrega com o orçamento já Rejeitado termina sem erro")]
    public async Task EstoqueBaixaFalhou_Reentrega_NaoGeraErro()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var cenario = await AbrirOsComOrcamentoEnviadoAsync(oficina, "EstoqueReentrega");
        var harness = _factory.CriarHarness();
        var falha = new EstoqueBaixaFalhouIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, cenario.OsId, cenario.OrcamentoId, "estoque insuficiente");

        await harness.Bus.Publish(falha);
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, cenario.OsId, "Rejeitado");
        var reentrega = falha with { EventId = Guid.NewGuid() };
        await harness.Bus.Publish(reentrega);

        await AguardarConsumoSemErroAsync(harness, reentrega);
        Assert.False(harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == cenario.OsId).Any());
    }

    /// <summary>
    /// Aguarda o consumer terminar de processar o evento e confirma que não falhou. O
    /// <c>Consumed.Any</c> do harness desiste cedo demais quando já há outras mensagens consumidas.
    /// </summary>
    private static async Task AguardarConsumoSemErroAsync<T>(ITestHarness harness, T evento)
        where T : class, SharedKernel.Domain.IIntegrationEvent
    {
        await OficinaMecanicaWebApplicationFactory.AguardarAsync(
            () => Task.FromResult(harness.Consumed.Select<T>(x => x.Context.Message.EventId == evento.EventId).Any()),
            $"{typeof(T).Name} {evento.EventId} consumido");

        Assert.All(
            harness.Consumed.Select<T>(x => x.Context.Message.EventId == evento.EventId),
            consumido => Assert.Null(consumido.Exception));
    }

    // ── E-mail do orçamento não derruba a Saga ──────────────────────────────

    [Fact(DisplayName = "Cadastro fora do ar ao montar o e-mail → abertura responde sucesso e OrcamentoGerado é publicado")]
    public async Task CadastroForaDoAr_NaMontagemDoEmail_NaoImpedeAPublicacaoDoOrcamentoGerado()
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome("Peça Email"), preco: 25.00m, estoque: 10, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Cliente Email"), email: TestData.Email("email.falha"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome("Serviço Email"), preco: 60.00m);
        // A validação da abertura consulta o cliente uma vez; a montagem do e-mail, em seguida, já encontra o Cadastro fora do ar.
        _factory.ServicosExternos.SimularCadastroIndisponivelApos(clienteId, consultasComSucesso: 1);

        var response = await oficina.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var osId = (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;
        var harness = _factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));
        Assert.Equal("AguardandoAprovacao", await StatusDaOsAsync(oficina, osId));
    }

    private sealed record IdDto(Guid Id);
}
