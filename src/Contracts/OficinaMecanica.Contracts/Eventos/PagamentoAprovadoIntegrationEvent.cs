using SharedKernel.Domain;

namespace OficinaMecanica.Contracts.IntegrationEvents;

public sealed record PagamentoAprovadoIntegrationEvent(
    Guid EventId, DateTime OcorridoEm,
    Guid PagamentoId, Guid OrdemServicoId, Guid OrcamentoId) : IIntegrationEvent;
