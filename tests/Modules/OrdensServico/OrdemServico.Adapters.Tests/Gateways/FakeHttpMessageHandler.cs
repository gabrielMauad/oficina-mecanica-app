using System.Net;
using System.Text;

namespace OrdemServico.Adapters.Tests.Gateways;

/// <summary>
/// Handler que responde com um status/corpo fixos e registra as requisições recebidas.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string? _json;

    public List<HttpRequestMessage> Requisicoes { get; } = [];

    private FakeHttpMessageHandler(HttpStatusCode status, string? json)
    {
        _status = status;
        _json = json;
    }

    public static FakeHttpMessageHandler Ok(string json) => new(HttpStatusCode.OK, json);

    public static FakeHttpMessageHandler Status(HttpStatusCode status) => new(status, null);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requisicoes.Add(request);

        var response = new HttpResponseMessage(_status);
        if (_json is not null)
            response.Content = new StringContent(_json, Encoding.UTF8, "application/json");

        return Task.FromResult(response);
    }

    public HttpClient CriarClient() => new(this) { BaseAddress = new Uri("http://servico-fake:8080/") };
}
