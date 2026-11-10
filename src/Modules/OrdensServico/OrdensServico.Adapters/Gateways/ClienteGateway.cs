using OrdensServico.Adapters.Gateways.Http;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;

namespace OrdensServico.Adapters.Gateways;

public sealed class ClienteGateway : IClienteGateway
{
    private readonly HttpClient _httpClient;

    public ClienteGateway(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<bool> ExisteEAtivo(Guid clienteId, CancellationToken ct)
    {
        var cliente = await ObterCliente(clienteId, ct);
        return cliente is { Ativo: true };
    }

    public async Task<ClienteInfo?> ObterInfo(Guid clienteId, CancellationToken ct)
    {
        var cliente = await ObterCliente(clienteId, ct);
        return cliente is null ? null : new ClienteInfo(cliente.Nome, cliente.Email);
    }

    private Task<ClienteResposta?> ObterCliente(Guid clienteId, CancellationToken ct) =>
        _httpClient.ObterOuNuloAsync<ClienteResposta>($"api/v1/clientes/{clienteId}", ct);
}
