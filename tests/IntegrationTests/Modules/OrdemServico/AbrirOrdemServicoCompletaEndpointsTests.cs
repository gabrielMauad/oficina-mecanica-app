using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using OficinaMecanica.Contracts.IntegrationEvents;

namespace IntegrationTests.Modules.OrdemServico;

[Collection("Integration")]
public class AbrirOrdemServicoCompletaEndpointsTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    public AbrirOrdemServicoCompletaEndpointsTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // POST /api/v1/ordens-servico/completa
    // =========================================================================

    [Fact(DisplayName = "POST /completa: cria OS direto em AguardandoAprovacao, com orçamento Enviado e publica OrcamentoGerado")]
    public async Task AbrirCompleta_ComDadosValidos_CriaOsAguardandoAprovacaoComOrcamentoEnviado()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        const int quantidadeUsada = 3;

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Fluxo Completo Cliente"), email: TestData.Email("fluxo.completo"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Alinhamento e Balanceamento OS Completa"), preco: 120.00m);

        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Pastilha de Freio Completa"), preco: 60.00m, estoque: 10, unidade: "Par");

        // Act — abrir OS já com serviços e peças definidos
        var abrirResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = quantidadeUsada } }
        });

        Assert.Equal(HttpStatusCode.Created, abrirResponse.StatusCode);
        Assert.NotNull(abrirResponse.Headers.Location);

        var os = await abrirResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        Assert.NotNull(os);
        Assert.Equal("AguardandoAprovacao", os.Status);
        Assert.Null(os.DescricaoDiagnostico);
        var osId = os.Id;

        // GET /{id}/status
        var statusResponse = await client.GetAsync($"/api/v1/ordens-servico/{osId}/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<StatusDto>(JsonOptions);
        Assert.Equal("AguardandoAprovacao", status!.Status);

        // Orçamento consta como Enviado (handler EnviarOrcamentoAoCliente reagiu ao evento)
        var osCompleta = await ObterOsAsync(client, osId);
        Assert.NotEmpty(osCompleta.Orcamentos);
        Assert.Equal("Enviado", osCompleta.Orcamentos[0].Status);

        // O estoque é baixado pelo serviço PecasInsumos ao consumir o evento publicado pela OS
        var harness = _factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));

        var evento = harness.Published.Select<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId).Single().Context.Message;
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(pecaId, peca.PecaInsumoId);
        Assert.Equal(quantidadeUsada, peca.Quantidade);
    }

    [Fact(DisplayName = "POST /completa: OS aberta pelo fluxo novo reaproveita aprovar → pagamento aprovado → finalizar → concluir sem alterações")]
    public async Task AbrirCompleta_SeguidoDoCicloDeVidaCompleto_FuncionaSemAlteracoes()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Ciclo Completo Cliente"), email: TestData.Email("ciclo.completo"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Revisão de Freios Ciclo Completo OS"), preco: 90.00m);

        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Disco de Freio Dianteiro"), preco: 45.00m, estoque: 5, unidade: "Unidade");

        var abrirResponse = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });
        abrirResponse.EnsureSuccessStatusCode();
        var osId = (await abrirResponse.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions))!.Id;

        // Rota exige papel Cliente (RFC-001 §4.2) — usa um client autenticado como o dono da OS
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(clienteId);
        var aprovarResponse = await clienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/aprovar-orcamento", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, aprovarResponse.StatusCode);

        // A execução começa pelo evento de pagamento aprovado (não há rota /executar)
        await _factory.AprovarPagamentoEAguardarExecucaoAsync(client, osId);

        var finalizarResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/finalizar", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, finalizarResponse.StatusCode);

        var concluirResponse = await client.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/concluir", EmptyJsonContent());
        Assert.Equal(HttpStatusCode.OK, concluirResponse.StatusCode);

        var osFinal = await ObterOsAsync(client, osId);
        Assert.Equal("Entregue", osFinal.Status);
        Assert.NotNull(osFinal.EntregueEm);
    }

    // =========================================================================
    // GET /api/v1/ordens-servico/acompanhamento
    // =========================================================================

    [Fact(DisplayName = "GET /acompanhamento: retorna OS ativas ordenadas por prioridade, mais antigas primeiro, sem Finalizada/Entregue")]
    public async Task ListarParaAcompanhamento_RetornaApenasOsAtivasOrdenadasPorPrioridade()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(nome: TestData.Nome("Acompanhamento Cliente"), email: TestData.Email("acompanhamento"));

        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var servicoId = _factory.ServicosExternos.AdicionarServico(nome: TestData.Nome("Revisão Acompanhamento"), preco: 70.00m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(nome: TestData.Nome("Peça Acompanhamento"), preco: 30.00m, estoque: 20, unidade: "Unidade");

        // OS 1: fica em Recebida
        var os1Id = await AbrirOsVaziaAsync(client, clienteId, veiculoId);

        // OS 2: avança até EmDiagnostico
        var os2Id = await AbrirOsVaziaAsync(client, clienteId, veiculoId);
        (await client.PatchAsync($"/api/v1/ordens-servico/{os2Id}/iniciar-diagnostico", EmptyJsonContent()))
            .EnsureSuccessStatusCode();

        // OS 3: aberta pelo fluxo novo → nasce direto em AguardandoAprovacao
        var os3Response = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });
        os3Response.EnsureSuccessStatusCode();
        var os3Id = (await os3Response.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions))!.Id;

        // OS 4: avança até Entregue → deve ficar de fora da listagem
        var os4Response = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 1 } }
        });
        os4Response.EnsureSuccessStatusCode();
        var os4Id = (await os4Response.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions))!.Id;

        // aprovar-orcamento exige papel Cliente (RFC-001 §4.2)
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(clienteId);
        (await clienteClient.PatchAsync($"/api/v1/ordens-servico/{os4Id}/aprovar-orcamento", EmptyJsonContent())).EnsureSuccessStatusCode();
        await _factory.AprovarPagamentoEAguardarExecucaoAsync(client, os4Id);
        (await client.PatchAsync($"/api/v1/ordens-servico/{os4Id}/finalizar", EmptyJsonContent())).EnsureSuccessStatusCode();
        (await client.PatchAsync($"/api/v1/ordens-servico/{os4Id}/concluir", EmptyJsonContent())).EnsureSuccessStatusCode();

        // Act — /acompanhamento exige papel Cliente e é filtrado pelo `sub` do token (o
        // dono das OS criadas acima)
        var acompanhamentoResponse = await clienteClient.GetAsync("/api/v1/ordens-servico/acompanhamento");
        Assert.Equal(HttpStatusCode.OK, acompanhamentoResponse.StatusCode);

        var lista = await acompanhamentoResponse.Content.ReadFromJsonAsync<List<OrdemServicoDto>>(JsonOptions);
        Assert.NotNull(lista);

        // Entregue não aparece
        Assert.DoesNotContain(lista, x => x.Id == os4Id);

        var ids = lista.Select(x => x.Id).ToList();
        Assert.Contains(os1Id, ids);
        Assert.Contains(os2Id, ids);
        Assert.Contains(os3Id, ids);

        // Prioridade: AguardandoAprovacao (os3) antes de EmDiagnostico (os2) antes de Recebida (os1)
        var idx1 = ids.IndexOf(os1Id);
        var idx2 = ids.IndexOf(os2Id);
        var idx3 = ids.IndexOf(os3Id);
        Assert.True(idx3 < idx2, "AguardandoAprovacao deveria vir antes de EmDiagnostico");
        Assert.True(idx2 < idx1, "EmDiagnostico deveria vir antes de Recebida");
    }

    // ── Helpers privados ──

    private async Task<Guid> AbrirOsVaziaAsync(HttpClient client, Guid clienteId, Guid veiculoId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        response.EnsureSuccessStatusCode();
        var os = await response.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        return os!.Id;
    }

    private async Task<OrdemServicoDto> ObterOsAsync(HttpClient client, Guid osId)
    {
        var response = await client.GetAsync($"/api/v1/ordens-servico/{osId}");
        response.EnsureSuccessStatusCode();
        var os = await response.Content.ReadFromJsonAsync<OrdemServicoDto>(JsonOptions);
        return os!;
    }

    // ── DTOs locais para desserialização ──


    private sealed record StatusDto(Guid Id, string Status);


    private sealed record OrdemServicoDto(
        Guid Id,
        Guid ClienteId,
        Guid VeiculoId,
        string Status,
        string? DescricaoDiagnostico,
        DateTime? NotificadoEm,
        DateTime? EntregueEm,
        DateTime CriadoEm,
        DateTime AtualizadoEm,
        ItemServicoDto[] ItensServico,
        ItemPecaDto[] ItensPeca,
        OrcamentoDto[] Orcamentos);

    private sealed record ItemServicoDto(
        Guid ServicoId,
        int Quantidade,
        decimal PrecoUnitarioSnapshot);

    private sealed record ItemPecaDto(
        Guid PecaInsumoId,
        int Quantidade,
        decimal PrecoUnitarioSnapshot);

    private sealed record OrcamentoDto(
        decimal ValorTotal,
        string Status,
        DateTime DataGeracao,
        DateTime? DataEnvio,
        DateTime? DataAprovacao);
}
