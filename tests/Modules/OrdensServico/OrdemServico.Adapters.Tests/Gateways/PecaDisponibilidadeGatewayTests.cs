using System.Net;
using OrdensServico.Adapters.Gateways;

namespace OrdemServico.Adapters.Tests.Gateways;

public class PecaDisponibilidadeGatewayTests
{
    private static readonly Guid PecaId = Guid.NewGuid();

    [Fact]
    public async Task Verificar_Disponivel_RetornaDisponibilidadeEChamaARotaComQuantidade()
    {
        var handler = FakeHttpMessageHandler.Ok("""{"disponivel":true,"precoUnitario":35.00}""");
        var gateway = new PecaDisponibilidadeGateway(handler.CriarClient());

        var resultado = await gateway.Verificar(PecaId, 3, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.True(resultado.Disponivel);
        Assert.Equal(35.00m, resultado.PrecoUnitario);
        Assert.Equal($"http://servico-fake:8080/api/v1/pecas-insumos/{PecaId}/disponibilidade?quantidade=3",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task Verificar_Indisponivel_RetornaDisponivelFalse()
    {
        var gateway = new PecaDisponibilidadeGateway(
            FakeHttpMessageHandler.Ok("""{"disponivel":false,"precoUnitario":35.00}""").CriarClient());

        var resultado = await gateway.Verificar(PecaId, 99, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.False(resultado.Disponivel);
    }

    [Fact]
    public async Task Verificar_404_RetornaNull()
    {
        var gateway = new PecaDisponibilidadeGateway(
            FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.Null(await gateway.Verificar(PecaId, 1, CancellationToken.None));
    }
}
