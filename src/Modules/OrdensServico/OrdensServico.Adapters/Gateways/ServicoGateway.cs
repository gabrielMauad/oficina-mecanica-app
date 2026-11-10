using OrdensServico.Adapters.Gateways.Http;
using OrdensServico.Application.Gateways;

namespace OrdensServico.Adapters.Gateways;

public sealed class ServicoGateway : IServicoGateway
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<Guid, ServicoResposta?> _cache = new();

    public ServicoGateway(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<decimal?> ObterPreco(Guid servicoId, CancellationToken ct)
    {
        var servico = await ObterServico(servicoId, ct);
        return servico?.Preco;
    }

    public async Task<string?> ObterNome(Guid servicoId, CancellationToken ct)
    {
        var servico = await ObterServico(servicoId, ct);
        return servico?.Nome;
    }

    private async Task<ServicoResposta?> ObterServico(Guid servicoId, CancellationToken ct)
    {
        if (!_cache.TryGetValue(servicoId, out var servico))
        {
            servico = await _httpClient.ObterOuNuloAsync<ServicoResposta>($"api/v1/servicos/{servicoId}", ct);
            _cache[servicoId] = servico;
        }
        return servico;
    }
}
