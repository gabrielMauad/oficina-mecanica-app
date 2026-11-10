using System.Net;
using OrdensServico.Adapters.Gateways;

namespace OrdemServico.Adapters.Tests.Gateways;

public class VeiculoGatewayTests
{
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static string Json => $$"""
        {"id":"{{VeiculoId}}","placa":"ABC1D23","modelo":"Gol","marca":"Volkswagen","ano":2019,
         "clienteId":"{{ClienteId}}","cadastradoEm":"2026-01-01T00:00:00Z","atualizadoEm":"2026-01-01T00:00:00Z"}
        """;

    [Fact]
    public async Task ExisteEPertenceAoCliente_MesmoCliente_RetornaTrueEChamaARotaDoVeiculo()
    {
        var handler = FakeHttpMessageHandler.Ok(Json);
        var gateway = new VeiculoGateway(handler.CriarClient());

        var resultado = await gateway.ExisteEPertenceAoCliente(VeiculoId, ClienteId, CancellationToken.None);

        Assert.True(resultado);
        Assert.Equal($"http://servico-fake:8080/api/v1/veiculos/{VeiculoId}",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task ExisteEPertenceAoCliente_OutroCliente_RetornaFalse()
    {
        var gateway = new VeiculoGateway(FakeHttpMessageHandler.Ok(Json).CriarClient());

        Assert.False(await gateway.ExisteEPertenceAoCliente(VeiculoId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ExisteEPertenceAoCliente_404_RetornaFalse()
    {
        var gateway = new VeiculoGateway(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.False(await gateway.ExisteEPertenceAoCliente(VeiculoId, ClienteId, CancellationToken.None));
    }

    [Fact]
    public async Task ObterPlaca_Sucesso_RetornaPlaca()
    {
        var gateway = new VeiculoGateway(FakeHttpMessageHandler.Ok(Json).CriarClient());

        Assert.Equal("ABC1D23", await gateway.ObterPlaca(VeiculoId, CancellationToken.None));
    }

    [Fact]
    public async Task ObterPlaca_404_RetornaNull()
    {
        var gateway = new VeiculoGateway(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound).CriarClient());

        Assert.Null(await gateway.ObterPlaca(VeiculoId, CancellationToken.None));
    }
}
