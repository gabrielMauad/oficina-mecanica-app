using SharedKernel.Domain;

namespace OficinaMecanica.Contracts.IntegrationEvents;

public sealed record PagamentoRecusadoIntegrationEvent(
    Guid EventId, DateTime OcorridoEm,
    Guid PagamentoId, Guid OrdemServicoId, Guid OrcamentoId,
    string Motivo) : IIntegrationEvent;
