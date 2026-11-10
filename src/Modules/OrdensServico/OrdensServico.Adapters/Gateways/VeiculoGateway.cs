using OrdensServico.Adapters.Gateways.Http;
using OrdensServico.Application.Gateways;

namespace OrdensServico.Adapters.Gateways;

public sealed class VeiculoGateway : IVeiculoGateway
{
    private readonly HttpClient _httpClient;

    public VeiculoGateway(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<bool> ExisteEPertenceAoCliente(Guid veiculoId, Guid clienteId, CancellationToken ct)
    {
        var veiculo = await ObterVeiculo(veiculoId, ct);
        return veiculo is not null && veiculo.ClienteId == clienteId;
    }

    public async Task<string?> ObterPlaca(Guid veiculoId, CancellationToken ct)
    {
        var veiculo = await ObterVeiculo(veiculoId, ct);
        return veiculo?.Placa;
    }

    private Task<VeiculoResposta?> ObterVeiculo(Guid veiculoId, CancellationToken ct) =>
        _httpClient.ObterOuNuloAsync<VeiculoResposta>($"api/v1/veiculos/{veiculoId}", ct);
}
