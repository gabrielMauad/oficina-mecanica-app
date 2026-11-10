using Microsoft.AspNetCore.Http;
using OrdensServico.Infrastructure.Http;

namespace OrdemServico.Infrastructure.Tests.Http;

public class EncaminharAuthorizationHandlerTests
{
    private sealed class CapturaRequisicaoHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Recebida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recebida = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class HttpContextAccessorFake(HttpContext? contexto) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = contexto;
    }

    private static HttpContext ContextoComAuthorization(string? valor)
    {
        var contexto = new DefaultHttpContext();
        if (valor is not null)
            contexto.Request.Headers.Authorization = valor;
        return contexto;
    }

    private static (HttpMessageInvoker Invoker, CapturaRequisicaoHandler Captura) Montar(HttpContext? contexto)
    {
        var captura = new CapturaRequisicaoHandler();
        var handler = new EncaminharAuthorizationHandler(new HttpContextAccessorFake(contexto))
        {
            InnerHandler = captura
        };
        return (new HttpMessageInvoker(handler), captura);
    }

    [Fact]
    public async Task Send_RequisicaoDeEntradaComAuthorization_RepassaOHeaderNaChamadaDeSaida()
    {
        var (invoker, captura) = Montar(ContextoComAuthorization("Bearer token-da-oficina"));

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://cadastro/api/v1/clientes/1"), CancellationToken.None);

        Assert.Equal("Bearer token-da-oficina", captura.Recebida!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Send_RequisicaoDeEntradaSemAuthorization_NaoAdicionaHeader()
    {
        var (invoker, captura) = Montar(ContextoComAuthorization(null));

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://cadastro/api/v1/clientes/1"), CancellationToken.None);

        Assert.Null(captura.Recebida!.Headers.Authorization);
    }

    [Fact]
    public async Task Send_SemHttpContext_SegueSemHeader()
    {
        var (invoker, captura) = Montar(contexto: null);

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://cadastro/api/v1/clientes/1"), CancellationToken.None);

        Assert.Null(captura.Recebida!.Headers.Authorization);
    }

    [Fact]
    public async Task Send_ChamadaJaComAuthorization_SubstituiPeloDaRequisicaoDeEntrada()
    {
        var (invoker, captura) = Montar(ContextoComAuthorization("Bearer token-de-entrada"));
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "http://cadastro/api/v1/clientes/1");
        requisicao.Headers.TryAddWithoutValidation("Authorization", "Bearer outro");

        await invoker.SendAsync(requisicao, CancellationToken.None);

        Assert.Equal("Bearer token-de-entrada", captura.Recebida!.Headers.Authorization!.ToString());
    }
}
