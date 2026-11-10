using System.Net;
using OrdensServico.Adapters.Gateways;

namespace OrdemServico.Adapters.Tests.Gateways;

public class PecaInsumoInfoGatewayTests
{
    private static readonly Guid PecaId = Guid.NewGuid();

    [Fact]
    public async Task Obter_Sucesso_RetornaNomeEUnidadeEChamaARotaDaPeca()
    {
        var handler = FakeHttpMessageHandler.Ok($$"""
            {"id":"{{PecaId}}","nome":"Óleo 5W30","descricao":null,"precoUnitario":35.00,
             "quantidadeEmEstoque":10,"unidadeDeMedida":"Litro","ativo":true,
             "cadastradoEm":"2026-01-01T00:00:00Z","atualizadoEm":"2026-01-01T00:00:00Z"}
            """);
        var gateway = new PecaInsumoInfoGateway(handler.CriarClient());

        var info = await gateway.Obter(PecaId, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("Óleo 5W30", info.Nome);
        Assert.Equal("Litro", info.UnidadeMedida);
        Assert.Equal($"http://servico-fake:8080/api/v1/pecas-insumos/{PecaId}",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task Obter_404_RetornaNull()
    {
        var gateway = new PecaInsumoInfoGateway(
            FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.Null(await gateway.Obter(PecaId, CancellationToken.None));
    }
}
