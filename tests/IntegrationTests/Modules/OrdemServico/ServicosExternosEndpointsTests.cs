using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Infrastructure;

namespace IntegrationTests.Modules.OrdemServico;

/// <summary>
/// Comportamento da OS diante dos serviços Cadastro e PecasInsumos (atendidos pelo fake).
/// </summary>
[Collection("Integration")]
public class ServicosExternosEndpointsTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    public ServicosExternosEndpointsTests(OficinaMecanicaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "POST /ordens-servico — consulta Cadastro repassando o Authorization da requisição de entrada")]
    public async Task CriarOS_ConsultaCadastroComOAuthorizationDaRequisicao()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Cliente Repasse"), email: TestData.Email("cliente.repasse"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);

        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var chamadas = _factory.ServicosExternos.Requisicoes
            .Where(r => r.CaminhoEQuery.EndsWith(clienteId.ToString()) || r.CaminhoEQuery.EndsWith(veiculoId.ToString()))
            .ToList();
        Assert.Contains(chamadas, r => r.CaminhoEQuery == $"/api/v1/clientes/{clienteId}");
        Assert.Contains(chamadas, r => r.CaminhoEQuery == $"/api/v1/veiculos/{veiculoId}");
        Assert.All(chamadas, r =>
        {
            Assert.Equal("GET", r.Metodo);
            Assert.Equal("Bearer", r.Authorization?.Scheme);
            Assert.Equal(token, r.Authorization?.Parameter);
        });
    }

    [Fact(DisplayName = "POST /ordens-servico — cliente inexistente no Cadastro (404) não cria a OS")]
    public async Task CriarOS_ClienteInexistenteNoCadastro_NaoCriaAOS()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico", new
        {
            ClienteId = Guid.NewGuid(),
            VeiculoId = Guid.NewGuid()
        });

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(DisplayName = "POST /ordens-servico/completa — peça sem estoque suficiente (PecasInsumos) não cria a OS")]
    public async Task AbrirCompleta_PecaIndisponivel_NaoCriaAOS()
    {
        var token = await _factory.GetAuthTokenAsync();
        using var client = _factory.CreateAuthenticatedClient(token);

        var clienteId = _factory.ServicosExternos.AdicionarCliente(
            nome: TestData.Nome("Cliente Sem Estoque"), email: TestData.Email("cliente.sem.estoque"));
        var veiculoId = _factory.ServicosExternos.AdicionarVeiculo(
            placa: TestData.PlacaMercosul(), clienteId: clienteId);
        var servicoId = _factory.ServicosExternos.AdicionarServico(
            nome: TestData.Nome("Serviço Sem Estoque"), preco: 50.00m);
        var pecaId = _factory.ServicosExternos.AdicionarPeca(
            nome: TestData.Nome("Peça Escassa"), preco: 10.00m, estoque: 1, unidade: "Unidade");

        var response = await client.PostAsJsonAsync("/api/v1/ordens-servico/completa", new
        {
            ClienteId = clienteId,
            VeiculoId = veiculoId,
            Servicos = new[] { new { ServicoId = servicoId, Quantidade = 1 } },
            Pecas = new[] { new { PecaInsumoId = pecaId, Quantidade = 5 } }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
