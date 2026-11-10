using System.Net;
using OrdensServico.Adapters.Gateways;

namespace OrdemServico.Adapters.Tests.Gateways;

public class ServicoGatewayTests
{
    private static readonly Guid ServicoId = Guid.NewGuid();

    private static string Json => $$"""
        {"id":"{{ServicoId}}","nome":"Troca de Óleo","descricao":null,"preco":150.50,"ativo":true,
         "cadastradoEm":"2026-01-01T00:00:00Z","atualizadoEm":"2026-01-01T00:00:00Z"}
        """;

    [Fact]
    public async Task ObterPreco_Sucesso_RetornaPrecoEChamaARotaDoServico()
    {
        var handler = FakeHttpMessageHandler.Ok(Json);
        var gateway = new ServicoGateway(handler.CriarClient());

        var preco = await gateway.ObterPreco(ServicoId, CancellationToken.None);

        Assert.Equal(150.50m, preco);
        Assert.Equal($"http://servico-fake:8080/api/v1/servicos/{ServicoId}",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task ObterNome_Sucesso_RetornaNome()
    {
        var gateway = new ServicoGateway(FakeHttpMessageHandler.Ok(Json).CriarClient());

        Assert.Equal("Troca de Óleo", await gateway.ObterNome(ServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task ObterPrecoENome_MesmoServico_ConsultaOServicoUmaUnicaVez()
    {
        var handler = FakeHttpMessageHandler.Ok(Json);
        var gateway = new ServicoGateway(handler.CriarClient());

        await gateway.ObterPreco(ServicoId, CancellationToken.None);
        await gateway.ObterNome(ServicoId, CancellationToken.None);

        Assert.Single(handler.Requisicoes);
    }

    [Fact]
    public async Task ObterPrecoENome_404_RetornamNull()
    {
        var gateway = new ServicoGateway(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.Null(await gateway.ObterPreco(ServicoId, CancellationToken.None));
        Assert.Null(await gateway.ObterNome(ServicoId, CancellationToken.None));
    }
}
