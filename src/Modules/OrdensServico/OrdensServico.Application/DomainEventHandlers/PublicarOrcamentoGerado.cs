using MediatR;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;

namespace OrdensServico.Application.DomainEventHandlers;

public sealed class PublicarOrcamentoGerado : INotificationHandler<OrcamentoGerado>
{
    private readonly IIntegrationEventBus _bus;
    private readonly IPendingIntegrationEvents _pendingEvents;

    public PublicarOrcamentoGerado(
        IIntegrationEventBus bus,
        IPendingIntegrationEvents pendingEvents
    )
    {
        _bus = bus;
        _pendingEvents = pendingEvents;
    }

    public Task Handle(OrcamentoGerado notification, CancellationToken ct)
    {
        IReadOnlyList<ItemPecaEventDto> itensPecaEventDto = [.. notification.Pecas.Select(p => new ItemPecaEventDto(p.PecaInsumoId, p.Quantidade))];
        OrcamentoGeradoIntegrationEvent integrationEvent = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            notification.OrdemServicoId.Value,
            notification.OrcamentoId.Value,
            itensPecaEventDto
        );
        _pendingEvents.Enqueue(ct => _bus.Publish(integrationEvent, ct));
        return Task.CompletedTask;
    }
}
