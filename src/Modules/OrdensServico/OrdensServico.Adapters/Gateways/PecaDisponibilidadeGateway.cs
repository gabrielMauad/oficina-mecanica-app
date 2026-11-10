using OrdensServico.Adapters.Gateways.Http;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;

namespace OrdensServico.Adapters.Gateways;

public sealed class PecaDisponibilidadeGateway : IPecaDisponibilidadeGateway
{
    private readonly HttpClient _httpClient;

    public PecaDisponibilidadeGateway(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<PecaDisponibilidade?> Verificar(Guid pecaInsumoId, int quantidade, CancellationToken ct)
    {
        var resposta = await _httpClient.ObterOuNuloAsync<DisponibilidadeResposta>(
            $"api/v1/pecas-insumos/{pecaInsumoId}/disponibilidade?quantidade={quantidade}", ct);

        return resposta is null
            ? null
            : new PecaDisponibilidade(resposta.Disponivel, resposta.PrecoUnitario);
    }
}
