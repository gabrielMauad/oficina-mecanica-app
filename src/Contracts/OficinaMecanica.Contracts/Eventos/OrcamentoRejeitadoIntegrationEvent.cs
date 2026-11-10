using SharedKernel.Domain;

namespace OficinaMecanica.Contracts.IntegrationEvents;

public sealed record OrcamentoRejeitadoIntegrationEvent(
    Guid EventId, DateTime OcorridoEm,
    Guid OrdemServicoId, Guid OrcamentoId,
    IReadOnlyList<ItemPecaEventDto> Pecas) : IIntegrationEvent;
