namespace OrdensServico.Application.Gateways;

public interface IServicoGateway
{
    Task<decimal?> ObterPreco(Guid servicoId, CancellationToken ct);
    Task<string?> ObterNome(Guid servicoId, CancellationToken ct);
    /// <summary>Indica se o serviço existe e está ativo no Cadastro (inexistente conta como não ativo).</summary>
    Task<bool> EstaAtivo(Guid servicoId, CancellationToken ct);
}
