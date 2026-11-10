using SharedKernel.Domain;

namespace OficinaMecanica.Contracts.IntegrationEvents;

public sealed record EstoqueBaixaFalhouIntegrationEvent(
    Guid EventId, DateTime OcorridoEm,
    Guid OrdemServicoId, Guid OrcamentoId,
    string Motivo) : IIntegrationEvent;
