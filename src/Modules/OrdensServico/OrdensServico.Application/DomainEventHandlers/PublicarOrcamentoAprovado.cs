using MediatR;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;

namespace OrdensServico.Application.DomainEventHandlers;

public sealed class PublicarOrcamentoAprovado : INotificationHandler<OrcamentoAprovado>
{
    private readonly IIntegrationEventBus _bus;
    private readonly IPendingIntegrationEvents _pendingEvents;

    public PublicarOrcamentoAprovado(
        IIntegrationEventBus bus,
        IPendingIntegrationEvents pendingEvents
    )
    {
        _bus = bus;
        _pendingEvents = pendingEvents;
    }

    public Task Handle(OrcamentoAprovado notification, CancellationToken ct)
    {
        OrcamentoAprovadoIntegrationEvent integrationEvent = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            notification.OrdemServicoId.Value,
            notification.OrcamentoId.Value,
            notification.ClienteId,
            notification.ValorTotal
        );
        _pendingEvents.Enqueue(ct => _bus.Publish(integrationEvent, ct));
        return Task.CompletedTask;
    }
}
