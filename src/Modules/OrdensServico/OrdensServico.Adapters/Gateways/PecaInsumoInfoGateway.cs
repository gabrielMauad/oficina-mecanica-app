using OrdensServico.Adapters.Gateways.Http;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;

namespace OrdensServico.Adapters.Gateways;

public sealed class PecaInsumoInfoGateway : IPecaInsumoInfoGateway
{
    private readonly HttpClient _httpClient;

    public PecaInsumoInfoGateway(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<PecaInsumoInfo?> Obter(Guid pecaInsumoId, CancellationToken ct)
    {
        var peca = await _httpClient.ObterOuNuloAsync<PecaInsumoResposta>(
            $"api/v1/pecas-insumos/{pecaInsumoId}", ct);

        return peca is null ? null : new PecaInsumoInfo(peca.Nome, peca.UnidadeDeMedida);
    }
}
