using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OrdensServico.Contracts.Queries;

namespace IntegrationTests.Modules.OrdemServico;

/// <summary>
/// Queries públicas do módulo (<see cref="IOrdemServicoResumoQuery"/> e
/// <see cref="IListarOrdensPorClienteQuery"/>), resolvidas pelo DI e executadas contra o PostgreSQL real.
/// </summary>
[Collection("Integration")]
public class ConsultasEntreModulosTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConsultasEntreModulosTests(OficinaMecanicaWebApplicationFactory factory) => _factory = factory;

    private sealed record IdDto(Guid Id);

    private async Task<(Guid OsId, Guid ClienteId, Guid VeiculoId, Guid ServicoId, Guid PecaId)> AbrirOsAsync()
    {
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome("Peça consulta"), preco: 40.00m, estoque: 20, unidade: "Unidade");
        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Cliente consulta"), email: TestData.Email("consulta"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome("Serviço consulta"), preco: 100.00m);

        using var oficina = _factory.CreateAuthenticatedClient(await _factory.GetAuthTokenAsync());
        var response = await oficina.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 2 } }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var osId = (await response.Content.ReadFromJsonAsync<IdDto>(JsonOptions, TestContext.Current.CancellationToken))!.Id;
        return (osId, clienteId, veiculoId, servicoId, pecaId);
    }

    [Fact(DisplayName = "IOrdemServicoResumoQuery: OS existente → resumo com itens e orçamento")]
    public async Task ObterPorId_OsExistente_RetornaResumoCompleto()
    {
        var os = await AbrirOsAsync();
        using var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IOrdemServicoResumoQuery>();

        var resumo = await query.ObterPorId(os.OsId, TestContext.Current.CancellationToken);

        Assert.NotNull(resumo);
        Assert.Equal(os.OsId, resumo.Id);
        Assert.Equal(os.ClienteId, resumo.ClienteId);
        Assert.Equal(os.VeiculoId, resumo.VeiculoId);
        var servico = Assert.Single(resumo.ItensServico);
        Assert.Equal(os.ServicoId, servico.ServicoId);
        Assert.Equal(100.00m, servico.PrecoUnitarioSnapshot);
        var peca = Assert.Single(resumo.ItensPeca);
        Assert.Equal(os.PecaId, peca.PecaInsumoId);
        Assert.Equal(2, peca.Quantidade);
        var orcamento = Assert.Single(resumo.Orcamentos);
        Assert.Equal(180.00m, orcamento.ValorTotal);
    }

    [Fact(DisplayName = "IOrdemServicoResumoQuery: id inexistente → null")]
    public async Task ObterPorId_OsInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IOrdemServicoResumoQuery>();

        var resumo = await query.ObterPorId(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(resumo);
    }

    [Fact(DisplayName = "IListarOrdensPorClienteQuery: lista só as OS do cliente informado")]
    public async Task Listar_RetornaApenasOrdensDoCliente()
    {
        var os = await AbrirOsAsync();
        await AbrirOsAsync(); // OS de outro cliente não deve aparecer
        using var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IListarOrdensPorClienteQuery>();

        var ordens = await query.Listar(os.ClienteId, TestContext.Current.CancellationToken);

        var unica = Assert.Single(ordens);
        Assert.Equal(os.OsId, unica.Id);
        Assert.Single(unica.ItensServico);
        Assert.Single(unica.ItensPeca);
        Assert.Single(unica.Orcamentos);
    }

    [Fact(DisplayName = "IListarOrdensPorClienteQuery: cliente sem OS → lista vazia")]
    public async Task Listar_ClienteSemOrdens_RetornaVazio()
    {
        using var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IListarOrdensPorClienteQuery>();

        var ordens = await query.Listar(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(ordens);
    }
}
