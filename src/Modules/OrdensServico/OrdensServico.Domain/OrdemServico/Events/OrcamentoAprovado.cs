using SharedKernel.Domain;

namespace OrdensServico.Domain.OrdemServico.Events;

public sealed record OrcamentoAprovado(
    OrdemServicoId OrdemServicoId,
    OrcamentoId OrcamentoId,
    Guid ClienteId,
    decimal ValorTotal,
    DateTime OcorridoEm
) : IDomainEvent;
