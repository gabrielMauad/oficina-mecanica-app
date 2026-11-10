using System.Net;
using System.Net.Http.Json;

namespace OrdensServico.Adapters.Gateways.Http;

internal static class HttpClientExtensions
{
    /// <summary>
    /// GET que devolve o corpo desserializado, ou <c>null</c> quando o serviço responde 404.
    /// Qualquer outro status de falha propaga <see cref="HttpRequestException"/>.
    /// </summary>
    public static async Task<T?> ObterOuNuloAsync<T>(
        this HttpClient client, string caminho, CancellationToken ct)
        where T : class
    {
        using var response = await client.GetAsync(caminho, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
