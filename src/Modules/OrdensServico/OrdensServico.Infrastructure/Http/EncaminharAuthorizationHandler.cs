using Microsoft.AspNetCore.Http;

namespace OrdensServico.Infrastructure.Http;

/// <summary>
/// Repassa o header <c>Authorization</c> da requisição de entrada para as chamadas HTTP de saída
/// aos serviços Cadastro e PecasInsumos (00-contratos.md §4.4). Sem requisição de entrada
/// (ex.: execução fora de um request HTTP) ou sem o header, a chamada segue sem credencial.
/// </summary>
public sealed class EncaminharAuthorizationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EncaminharAuthorizationHandler(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(authorization))
        {
            request.Headers.Remove("Authorization");
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
