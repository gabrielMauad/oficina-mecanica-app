using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Ordens.Commands.AprovarOrcamento;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamento;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;

namespace IntegrationTests.EventBus;

/// <summary>
/// Concorrência otimista da OS contra o PostgreSQL real: quando duas operações leem a mesma versão
/// da OS, só a primeira gravação vale. A perdedora recebe 409 na API (ou é reexecutada pelo retry
/// do consumer) e não publica evento algum. As corridas são provocadas no intervalo entre a leitura
/// e a gravação pelo <see cref="SimuladorDeConcorrencia"/>.
/// </summary>
[Collection("Integration")]
public class ConcorrenciaDaOrdemServicoTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    public ConcorrenciaDaOrdemServicoTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(Guid ClienteId, Guid OsId, Guid OrcamentoId);

    /// <summary>Abre uma OS pelo fluxo completo (orçamento Enviado, OrcamentoGerado publicado).</summary>
    private async Task<Cenario> AbrirOsComOrcamentoEnviadoAsync(string rotulo)
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            TestData.Nome($"Peça {rotulo}"), preco: 40m, estoque: 20, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            TestData.Nome($"Cliente {rotulo}"), TestData.Email(rotulo.ToLowerInvariant()));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(TestData.PlacaMercosul(), clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(TestData.Nome($"Serviço {rotulo}"), preco: 100m);

        var response = await oficina.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var osId = (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions))!.Id;

        var orcamentoId = await _factory.ObterOrcamentoIdPublicadoAsync(osId);
        return new Cenario(clienteId, osId, orcamentoId);
    }

    /// <summary>Executa o comando em um escopo próprio, como faria outra requisição ou um consumer.</summary>
    private async Task EnviarComoOutraOperacaoAsync<TResponse>(IRequest<TResponse> comando)
    {
        using var escopo = _factory.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<ISender>().Send(comando);
    }

    private async Task<(string StatusOs, string StatusOrcamento)> EstadoAsync(Cenario c)
    {
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        using var os = await OficinaMecanicaWebApplicationFactory.ObterOsJsonAsync(oficina, c.OsId);
        return (os.RootElement.GetProperty("status").GetString()!,
            os.RootElement.GetProperty("orcamentos")[0].GetProperty("status").GetString()!);
    }

    private static string CaminhoAprovar(Guid osId) => $"/api/v1/ordens-servico/{osId}/aprovar-orcamento";
    private static string CaminhoRejeitar(Guid osId) => $"/api/v1/ordens-servico/{osId}/rejeitar-orcamento";

    [Fact(DisplayName = "Aprovar × falha de estoque: a falha grava primeiro → aprovar recebe 409, não publica OrcamentoAprovado e o orçamento fica Rejeitado")]
    public async Task Aprovar_ConcorrenteComFalhaDeEstoque_Retorna409ENaoPublica()
    {
        var c = await AbrirOsComOrcamentoEnviadoAsync("AprovarVsFalha");
        _factory.Simulador.Armar(c.OsId, () => EnviarComoOutraOperacaoAsync(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(c.OsId, c.OrcamentoId)));

        using var cliente = _factory.CreateClienteAuthenticatedClient(c.ClienteId);
        var response = await cliente.PatchAsync(CaminhoAprovar(c.OsId), EmptyJsonContent());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(_factory.Simulador.FoiDisparada(c.OsId));
        var (statusOs, statusOrcamento) = await EstadoAsync(c);
        Assert.Equal("AguardandoAprovacao", statusOs);
        Assert.Equal("Rejeitado", statusOrcamento);
        Assert.False(await _factory.CriarHarness().Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));

        // Repetir a chamada reflete o novo estado: o orçamento já não está mais Enviado.
        var repeticao = await cliente.PatchAsync(CaminhoAprovar(c.OsId), EmptyJsonContent());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repeticao.StatusCode);
    }

    [Fact(DisplayName = "Rejeitar × rejeitar: a segunda gravação perde → 409 e um único OrcamentoRejeitado publicado")]
    public async Task Rejeitar_ConcorrenteComOutroRejeitar_Retorna409EPublicaUmSoEvento()
    {
        var c = await AbrirOsComOrcamentoEnviadoAsync("RejeitarVsRejeitar");
        _factory.Simulador.Armar(c.OsId, () => EnviarComoOutraOperacaoAsync(
            new RejeitarOrcamentoCommand(c.OsId, c.ClienteId)));

        using var cliente = _factory.CreateClienteAuthenticatedClient(c.ClienteId);
        var response = await cliente.PatchAsync(CaminhoRejeitar(c.OsId), EmptyJsonContent());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(_factory.Simulador.FoiDisparada(c.OsId));
        var harness = _factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));
        Assert.Single(harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));
        Assert.Equal("Rejeitado", (await EstadoAsync(c)).StatusOrcamento);
    }

    [Fact(DisplayName = "Aprovar × rejeitar: o rejeitar grava primeiro → aprovar recebe 409 e não aprova por cima")]
    public async Task Aprovar_ConcorrenteComRejeitar_Retorna409()
    {
        var c = await AbrirOsComOrcamentoEnviadoAsync("AprovarVsRejeitar");
        _factory.Simulador.Armar(c.OsId, () => EnviarComoOutraOperacaoAsync(
            new RejeitarOrcamentoCommand(c.OsId, c.ClienteId)));

        using var cliente = _factory.CreateClienteAuthenticatedClient(c.ClienteId);
        var response = await cliente.PatchAsync(CaminhoAprovar(c.OsId), EmptyJsonContent());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Rejeitado", (await EstadoAsync(c)).StatusOrcamento);
        Assert.False(await _factory.CriarHarness().Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou × aprovar: o consumer perde a corrida, o retry reaplica a regra e o orçamento termina Rejeitado, sem execução")]
    public async Task ConsumerDeEstoqueBaixaFalhou_PerdeACorridaParaAprovar_RetentaETerminaRejeitado()
    {
        var c = await AbrirOsComOrcamentoEnviadoAsync("FalhaVsAprovar");
        // A aprovação grava entre a leitura e a gravação do consumer.
        _factory.Simulador.Armar(c.OsId, () => EnviarComoOutraOperacaoAsync(
            new AprovarOrcamentoCommand(c.OsId, c.ClienteId)));
        var harness = _factory.CriarHarness();

        await harness.Bus.Publish(new EstoqueBaixaFalhouIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, c.OsId, c.OrcamentoId, "Estoque insuficiente."));

        // 1ª tentativa conflita (a aprovação venceu); a releitura no retry vê o orçamento Aprovado e o rejeita.
        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        await _factory.AguardarStatusDoOrcamentoAsync(oficina, c.OsId, "Rejeitado");
        Assert.True(_factory.Simulador.FoiDisparada(c.OsId));
        Assert.True(await harness.Published.Any<OrcamentoAprovadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));

        // O pagamento do orçamento já rejeitado não pode colocar a OS em execução.
        await harness.Bus.Publish(new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), c.OsId, c.OrcamentoId));
        await Task.Delay(500);
        var (statusOs, statusOrcamento) = await EstadoAsync(c);
        Assert.Equal("AguardandoAprovacao", statusOs);
        Assert.Equal("Rejeitado", statusOrcamento);
    }

    [Fact(DisplayName = "Rejeitar em paralelo (8 chamadas reais): um único 200, o restante 409/422, e um único OrcamentoRejeitado")]
    public async Task Rejeitar_EmParalelo_PermiteUmaUnicaRejeicao()
    {
        var c = await AbrirOsComOrcamentoEnviadoAsync("RejeitarParalelo");
        using var cliente = _factory.CreateClienteAuthenticatedClient(c.ClienteId);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => cliente.PatchAsync(CaminhoRejeitar(c.OsId), EmptyJsonContent()))));

        var codigos = respostas.Select(r => r.StatusCode).ToList();
        Assert.Single(codigos, HttpStatusCode.OK);
        Assert.All(codigos.Where(s => s != HttpStatusCode.OK),
            s => Assert.True(s is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, $"status inesperado: {s}"));

        var harness = _factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));
        Assert.Single(harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == c.OsId));
    }

    private sealed record IdDto(Guid Id);
}
