using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Bdd.Support;
using Reqnroll;
using SharedKernel.Domain;

namespace OrdensServico.Bdd.Steps;

[Binding]
public sealed class FluxoOrdemServicoSteps
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ContextoDoFluxo _ctx;

    // Último evento "recebido" de outro serviço (para confirmar que foi consumido sem erro).
    private IIntegrationEvent? _ultimoEventoRecebido;
    private HttpResponseMessage? _respostaDoDiagnostico;

    public FluxoOrdemServicoSteps(ContextoDoFluxo ctx) => _ctx = ctx;

    private static OficinaMecanicaWebApplicationFactory Factory => AmbienteBdd.Factory;

    private static HttpContent JsonVazio() => new StringContent("{}", Encoding.UTF8, "application/json");

    // ── Contexto ────────────────────────────────────────────────────────────

    [Given("um cliente com veículo, um serviço de {int} reais e uma peça de {int} reais com estoque suficiente")]
    public void DadoCadastrosBasicos(int precoServico, int precoPeca)
    {
        var cadastro = Factory.ServicosExternos;
        _ctx.ClienteId = cadastro.AdicionarCliente(TestData.Nome("Cliente BDD"), TestData.Email("cliente.bdd"));
        _ctx.VeiculoId = cadastro.AdicionarVeiculo(TestData.PlacaMercosul(), _ctx.ClienteId);
        _ctx.ServicoId = cadastro.AdicionarServico(TestData.Nome("Serviço BDD"), precoServico);
        _ctx.PecaId = cadastro.AdicionarPeca(TestData.Nome("Peça BDD"), precoPeca, estoque: 100, unidade: "Unidade");
    }

    [Given("a oficina autenticada")]
    public async Task DadoOficinaAutenticada() =>
        _ctx.Oficina = Factory.CreateAuthenticatedClient(await Factory.GetAuthTokenAsync());

    // ── Abertura e diagnóstico ──────────────────────────────────────────────

    [Given("que a oficina abriu a ordem de serviço completa com {int} serviço e {int} unidades da peça")]
    [When("a oficina abre a ordem de serviço completa com {int} serviço e {int} unidades da peça")]
    public async Task AbrirOrdemServicoCompleta(int servicos, int pecas)
    {
        var response = await _ctx.Oficina.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = _ctx.ClienteId,
            VeiculoId = _ctx.VeiculoId,
            Servicos = new[] { new { ServicoId = _ctx.ServicoId, Quantidade = servicos } },
            Pecas = new[] { new { PecaInsumoId = _ctx.PecaId, Quantidade = pecas } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _ctx.OsId = (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;

        await AguardarNovoOrcamentoGeradoAsync();
    }

    [Given("que a oficina registrou um novo diagnóstico com {int} serviço e {int} unidade da peça")]
    [When("a oficina registra um novo diagnóstico com {int} serviço e {int} unidade da peça")]
    public async Task RegistrarNovoDiagnostico(int servicos, int pecas)
    {
        _respostaDoDiagnostico = await _ctx.Oficina.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{_ctx.OsId}/registrar-diagnostico", new
            {
                DescricaoDiagnostico = "Novo diagnóstico após a rejeição do orçamento.",
                Servicos = new[] { new { ServicoId = _ctx.ServicoId, Quantidade = servicos } },
                Pecas = new[] { new { PecaInsumoId = _ctx.PecaId, Quantidade = pecas } }
            });

        if (_respostaDoDiagnostico.StatusCode == HttpStatusCode.OK)
            await AguardarNovoOrcamentoGeradoAsync();
    }

    /// <summary>
    /// Aguarda o <c>OrcamentoGerado</c> do novo orçamento (a PecasInsumos real baixaria o estoque a
    /// partir dele) e que o orçamento fique Enviado ao cliente.
    /// </summary>
    private async Task AguardarNovoOrcamentoGeradoAsync()
    {
        var harness = Factory.CriarHarness();
        var esperados = _ctx.OrcamentoIds.Count + 1;
        await OficinaMecanicaWebApplicationFactory.AguardarAsync(
            () => Task.FromResult(OrcamentosGeradosPublicados(harness).Count == esperados),
            $"OrcamentoGerado nº {esperados} da OS {_ctx.OsId} publicado");
        _ctx.OrcamentoIds.Add(OrcamentosGeradosPublicados(harness).Last().OrcamentoId);

        await Factory.AguardarStatusDoOrcamentoAsync(_ctx.Oficina, _ctx.OsId, "Enviado", _ctx.IndiceDoOrcamentoAtual);
    }

    private List<OrcamentoGeradoIntegrationEvent> OrcamentosGeradosPublicados(ITestHarness harness) =>
        harness.Published.Select<OrcamentoGeradoIntegrationEvent>(x => x.Context.Message.OrdemServicoId == _ctx.OsId)
            .Select(x => x.Context.Message).ToList();

    // ── Ações do cliente ────────────────────────────────────────────────────

    [Given("que o cliente aprovou o orçamento")]
    [Given("que o cliente aprovou o novo orçamento")]
    [When("o cliente aprova o orçamento")]
    public async Task ClienteAprovaOrcamento()
    {
        using var cliente = Factory.CreateClienteAuthenticatedClient(_ctx.ClienteId);
        var response = await cliente.PatchAsync(
            $"/api/v1/ordens-servico/{_ctx.OsId}/aprovar-orcamento", JsonVazio());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [When("o cliente rejeita o orçamento")]
    public async Task ClienteRejeitaOrcamento()
    {
        using var cliente = Factory.CreateClienteAuthenticatedClient(_ctx.ClienteId);
        var response = await cliente.PatchAsync(
            $"/api/v1/ordens-servico/{_ctx.OsId}/rejeitar-orcamento", JsonVazio());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Eventos dos outros serviços ─────────────────────────────────────────

    [When("chega o evento PagamentoAprovado do orçamento")]
    public Task ChegaPagamentoAprovado() => PublicarPagamentoAprovadoAsync(_ctx.OrcamentoAtualId);

    [When("chega o evento PagamentoAprovado do primeiro orçamento, já rejeitado")]
    public Task ChegaPagamentoAprovadoDoPrimeiroOrcamento() => PublicarPagamentoAprovadoAsync(_ctx.OrcamentoIds[0]);

    private async Task PublicarPagamentoAprovadoAsync(Guid orcamentoId)
    {
        var evento = new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), _ctx.OsId, orcamentoId);
        await Factory.CriarHarness().Bus.Publish(evento);
        _ultimoEventoRecebido = evento;
    }

    [When("chega o evento PagamentoRecusado do orçamento com o motivo {string}")]
    public Task ChegaPagamentoRecusado(string motivo) => PublicarPagamentoRecusadoAsync(motivo);

    [Given("que o pagamento do orçamento foi recusado")]
    public async Task DadoPagamentoRecusado()
    {
        await PublicarPagamentoRecusadoAsync("cc_rejected_other_reason");
        await Factory.AguardarStatusDoOrcamentoAsync(_ctx.Oficina, _ctx.OsId, "Rejeitado", _ctx.IndiceDoOrcamentoAtual);
    }

    private async Task PublicarPagamentoRecusadoAsync(string motivo)
    {
        var evento = new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), _ctx.OsId, _ctx.OrcamentoAtualId, motivo);
        await Factory.CriarHarness().Bus.Publish(evento);
        _ultimoEventoRecebido = evento;
    }

    [When("chega o evento EstoqueBaixaFalhou do orçamento com o motivo {string}")]
    public async Task ChegaEstoqueBaixaFalhou(string motivo)
    {
        var evento = new EstoqueBaixaFalhouIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, _ctx.OsId, _ctx.OrcamentoAtualId, motivo);
        await Factory.CriarHarness().Bus.Publish(evento);
        _ultimoEventoRecebido = evento;
    }

    // ── Ações da oficina ────────────────────────────────────────────────────

    [When("a oficina finaliza a ordem de serviço")]
    public async Task OficinaFinaliza()
    {
        var response = await _ctx.Oficina.PatchAsync($"/api/v1/ordens-servico/{_ctx.OsId}/finalizar", JsonVazio());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [When("a oficina conclui a ordem de serviço")]
    public async Task OficinaConclui()
    {
        var response = await _ctx.Oficina.PatchAsync($"/api/v1/ordens-servico/{_ctx.OsId}/concluir", JsonVazio());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Verificações de estado ──────────────────────────────────────────────

    [Then("a ordem de serviço está {string}")]
    public Task EntaoStatusDaOs(string status) =>
        Factory.AguardarStatusDaOsAsync(_ctx.Oficina, _ctx.OsId, status);

    [Then("o orçamento está {string}")]
    public Task EntaoStatusDoOrcamento(string status) =>
        Factory.AguardarStatusDoOrcamentoAsync(_ctx.Oficina, _ctx.OsId, status, _ctx.IndiceDoOrcamentoAtual);

    [Then("a ordem de serviço possui {int} orçamentos")]
    public async Task EntaoQuantidadeDeOrcamentos(int quantidade)
    {
        using var os = await OficinaMecanicaWebApplicationFactory.ObterOsJsonAsync(_ctx.Oficina, _ctx.OsId);
        Assert.Equal(quantidade, os.RootElement.GetProperty("orcamentos").GetArrayLength());
    }

    [Then("o cliente foi notificado")]
    public async Task EntaoClienteNotificado()
    {
        using var os = await OficinaMecanicaWebApplicationFactory.ObterOsJsonAsync(_ctx.Oficina, _ctx.OsId);
        Assert.NotEqual(JsonValueKind.Null, os.RootElement.GetProperty("notificadoEm").ValueKind);
    }

    [Then("o diagnóstico é aceito")]
    public void EntaoDiagnosticoAceito()
    {
        Assert.NotNull(_respostaDoDiagnostico);
        Assert.Equal(HttpStatusCode.OK, _respostaDoDiagnostico.StatusCode);
    }

    // ── Verificações de eventos publicados ──────────────────────────────────

    [Then("o evento OrcamentoGerado foi publicado com {int} unidades da peça")]
    public void EntaoOrcamentoGeradoPublicado(int quantidade)
    {
        var evento = OrcamentosGeradosPublicados(Factory.CriarHarness()).Single();
        Assert.Equal(_ctx.OrcamentoAtualId, evento.OrcamentoId);
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(_ctx.PecaId, peca.PecaInsumoId);
        Assert.Equal(quantidade, peca.Quantidade);
    }

    [Then("o evento OrcamentoAprovado foi publicado para o cliente com valor total de {int} reais")]
    public async Task EntaoOrcamentoAprovadoPublicado(int valorTotal)
    {
        var harness = Factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == _ctx.OsId));

        var evento = harness.Published.Select<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == _ctx.OsId).Single().Context.Message;
        Assert.Equal(_ctx.OrcamentoAtualId, evento.OrcamentoId);
        Assert.Equal(_ctx.ClienteId, evento.ClienteId);
        Assert.Equal(valorTotal, evento.ValorTotal);
    }

    [Then("o evento OrcamentoRejeitado foi publicado com {int} unidades da peça")]
    public async Task EntaoOrcamentoRejeitadoPublicado(int quantidade)
    {
        var harness = Factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == _ctx.OsId));

        var evento = harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == _ctx.OsId).Single().Context.Message;
        Assert.Equal(_ctx.OrcamentoAtualId, evento.OrcamentoId);
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(_ctx.PecaId, peca.PecaInsumoId);
        Assert.Equal(quantidade, peca.Quantidade);
    }

    [Then("nenhum evento OrcamentoRejeitado foi publicado")]
    public async Task EntaoNenhumOrcamentoRejeitadoPublicado()
    {
        var harness = Factory.CriarHarness();

        // A ausência só é determinística depois que o consumidor terminou: o OrcamentoRejeitado seria
        // publicado dentro dele, então, após o Consumed do último evento recebido, a checagem abaixo
        // (sem espera) não depende do tempo de inatividade do harness.
        await AguardarConsumoDoUltimoEventoAsync(harness);

        Assert.False(harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == _ctx.OsId
              && x.Context.Message.OrcamentoId == _ctx.OrcamentoAtualId).Any());
    }

    [Then("o evento foi consumido sem erro")]
    public async Task EntaoEventoConsumidoSemErro() =>
        await AguardarConsumoDoUltimoEventoAsync(Factory.CriarHarness());

    private async Task AguardarConsumoDoUltimoEventoAsync(ITestHarness harness)
    {
        var evento = _ultimoEventoRecebido ?? throw new InvalidOperationException("Nenhum evento foi enviado.");

        // Consumed.Select<T> exige o tipo concreto: despacha pelo tipo do evento enviado.
        switch (evento)
        {
            case PagamentoAprovadoIntegrationEvent e: await AguardarConsumoSemErroAsync(harness, e); break;
            case PagamentoRecusadoIntegrationEvent e: await AguardarConsumoSemErroAsync(harness, e); break;
            case EstoqueBaixaFalhouIntegrationEvent e: await AguardarConsumoSemErroAsync(harness, e); break;
            default: throw new NotSupportedException(evento.GetType().Name);
        }
    }

    private static async Task AguardarConsumoSemErroAsync<T>(ITestHarness harness, T evento)
        where T : class, IIntegrationEvent
    {
        await OficinaMecanicaWebApplicationFactory.AguardarAsync(
            () => Task.FromResult(harness.Consumed.Select<T>(x => x.Context.Message.EventId == evento.EventId).Any()),
            $"{typeof(T).Name} {evento.EventId} consumido");

        Assert.All(
            harness.Consumed.Select<T>(x => x.Context.Message.EventId == evento.EventId),
            consumido => Assert.Null(consumido.Exception));
    }

    private sealed record IdDto(Guid Id);
}
