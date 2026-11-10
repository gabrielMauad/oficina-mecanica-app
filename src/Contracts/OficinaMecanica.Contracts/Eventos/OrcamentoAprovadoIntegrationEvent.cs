using SharedKernel.Domain;

namespace OficinaMecanica.Contracts.IntegrationEvents;

public sealed record OrcamentoAprovadoIntegrationEvent(
    Guid EventId, DateTime OcorridoEm,
    Guid OrdemServicoId, Guid OrcamentoId,
    Guid ClienteId, decimal ValorTotal) : IIntegrationEvent;
