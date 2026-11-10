using System.Net;
using OrdensServico.Adapters.Gateways;

namespace OrdemServico.Adapters.Tests.Gateways;

public class ClienteGatewayTests
{
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static string Json(bool ativo) => $$"""
        {"id":"{{ClienteId}}","nome":"Ana Souza","documento":"12345678909","email":"ana@teste.com",
         "telefone":"31999990000","ativo":{{ativo.ToString().ToLowerInvariant()}},
         "cadastradoEm":"2026-01-01T00:00:00Z","atualizadoEm":"2026-01-01T00:00:00Z"}
        """;

    [Fact]
    public async Task ExisteEAtivo_ClienteAtivo_RetornaTrueEChamaARotaDoCliente()
    {
        var handler = FakeHttpMessageHandler.Ok(Json(ativo: true));
        var gateway = new ClienteGateway(handler.CriarClient());

        var resultado = await gateway.ExisteEAtivo(ClienteId, CancellationToken.None);

        Assert.True(resultado);
        var requisicao = Assert.Single(handler.Requisicoes);
        Assert.Equal(HttpMethod.Get, requisicao.Method);
        Assert.Equal($"http://servico-fake:8080/api/v1/clientes/{ClienteId}", requisicao.RequestUri!.ToString());
    }

    [Fact]
    public async Task ExisteEAtivo_ClienteInativo_RetornaFalse()
    {
        var gateway = new ClienteGateway(FakeHttpMessageHandler.Ok(Json(ativo: false)).CriarClient());

        Assert.False(await gateway.ExisteEAtivo(ClienteId, CancellationToken.None));
    }

    [Fact]
    public async Task ExisteEAtivo_404_RetornaFalse()
    {
        var gateway = new ClienteGateway(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.False(await gateway.ExisteEAtivo(ClienteId, CancellationToken.None));
    }

    [Fact]
    public async Task ObterInfo_Sucesso_RetornaNomeEEmail()
    {
        var gateway = new ClienteGateway(FakeHttpMessageHandler.Ok(Json(ativo: true)).CriarClient());

        var info = await gateway.ObterInfo(ClienteId, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("Ana Souza", info.Nome);
        Assert.Equal("ana@teste.com", info.Email);
    }

    [Fact]
    public async Task ObterInfo_404_RetornaNull()
    {
        var gateway = new ClienteGateway(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.Null(await gateway.ObterInfo(ClienteId, CancellationToken.None));
    }

    [Fact]
    public async Task ObterInfo_ErroDoServico_PropagaHttpRequestException()
    {
        var gateway = new ClienteGateway(
            FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError).CriarClient());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => gateway.ObterInfo(ClienteId, CancellationToken.None));
    }
}
