using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using OficinaMecanica.Contracts.IntegrationEvents;

namespace IntegrationTests.EventBus;

/// <summary>
/// A OS publica <see cref="OrcamentoGeradoIntegrationEvent"/> e <see cref="OrcamentoRejeitadoIntegrationEvent"/>
/// no barramento (MassTransit). A baixa e o estorno de estoque são efeitos do serviço PecasInsumos ao
/// consumir esses eventos — aqui só se verifica o que a OS publica.
/// </summary>
[Collection("Integration")]
public class EstoqueIntegrationEventTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Retorna um novo HttpContent com body vazio para PATCHes sem payload.
    /// HttpContent é descartável — não reutilizar a mesma instância entre requests.
    /// </summary>
    private static HttpContent EmptyJsonContent() =>
        new StringContent("{}", Encoding.UTF8, "application/json");

    public EstoqueIntegrationEventTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "RegistrarDiagnostico → publica OrcamentoGerado com as peças do orçamento")]
    public async Task RegistrarDiagnostico_DevePublicarOrcamentoGerado()
    {
        // ARRANGE
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);
        const int quantidadeUsada = 3;

        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome("Vela de Ignição NGK EventBus"), preco: 12.50m, estoque: 10, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Ana EventBus"), email: TestData.Email("ana.eventbus"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome("Troca de Velas"), preco: 80.00m);

        var osId = await CriarOrdemServicoAsync(client, clienteId, veiculoId);
        (await client.PatchAsync($"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent()))
            .EnsureSuccessStatusCode();

        // ACT — registrar o diagnóstico gera o orçamento e, após o commit, publica o evento
        var registrarResponse = await client.PatchAsJsonAsync(
            $"/api/v1/ordens-servico/{osId}/registrar-diagnostico", new
            {
                DescricaoDiagnostico = "Velas com desgaste excessivo. Substituir todas.",
                Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
                Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = quantidadeUsada } }
            });
        Assert.Equal(HttpStatusCode.OK, registrarResponse.StatusCode);

        // ASSERT
        var harness = _factory.CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));

        var evento = harness.Published.Select<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId).Single().Context.Message;
        Assert.NotEqual(Guid.Empty, evento.EventId);
        Assert.NotEqual(Guid.Empty, evento.OrcamentoId);
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(pecaId, peca.PecaInsumoId);
        Assert.Equal(quantidadeUsada, peca.Quantidade);
    }

    [Fact(DisplayName = "RejeitarOrcamento → publica OrcamentoRejeitado com as peças do orçamento")]
    public async Task RejeitarOrcamento_DevePublicarOrcamentoRejeitado()
    {
        // ARRANGE
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);
        const int quantidadeUsada = 5;

        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome("Correia Dentada Gates EventBus"), preco: 95.00m, estoque: 20, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Bruno Rejeicao"), email: TestData.Email("bruno.rejeicao"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome("Troca de Correia Dentada"), preco: 200.00m);

        var osId = await CriarOrdemServicoAsync(client, clienteId, veiculoId);
        (await client.PatchAsync($"/api/v1/ordens-servico/{osId}/iniciar-diagnostico", EmptyJsonContent()))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/v1/ordens-servico/{osId}/registrar-diagnostico", new
        {
            DescricaoDiagnostico = "Correia dentada com risco de ruptura.",
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = quantidadeUsada } }
        })).EnsureSuccessStatusCode();

        var harness = _factory.CriarHarness();
        Assert.False(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));

        // ACT — rota exige papel Cliente (RFC-001 §4.2): client autenticado como o dono da OS
        using var clienteClient = _factory.CreateClienteAuthenticatedClient(clienteId);
        (await clienteClient.PatchAsync(
            $"/api/v1/ordens-servico/{osId}/rejeitar-orcamento", EmptyJsonContent()))
            .EnsureSuccessStatusCode();

        // ASSERT
        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));

        var evento = harness.Published.Select<OrcamentoRejeitadoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId).Single().Context.Message;
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(pecaId, peca.PecaInsumoId);
        Assert.Equal(quantidadeUsada, peca.Quantidade);
    }

    private static async Task<Guid> CriarOrdemServicoAsync(HttpClient client, Guid clienteId, Guid veiculoId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions);
        return body!.Id;
    }

    private sealed record IdDto(Guid Id);
}
