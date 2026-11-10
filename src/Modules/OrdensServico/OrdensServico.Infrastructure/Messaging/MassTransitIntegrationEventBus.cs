using MassTransit;
using SharedKernel.Application;
using SharedKernel.Domain;

namespace OrdensServico.Infrastructure.Messaging;

/// <summary>
/// Adapter de <see cref="IIntegrationEventBus"/> sobre o MassTransit: publica o evento no
/// transporte configurado (SNS/SQS ou in-memory). O tipo publicado é sempre o tipo concreto do
/// evento, que é o que o MassTransit usa para rotear (nome completo do tipo).
/// </summary>
public sealed class MassTransitIntegrationEventBus : IIntegrationEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitIntegrationEventBus(IPublishEndpoint publishEndpoint) =>
        _publishEndpoint = publishEndpoint;

    public Task Publish<T>(T evento, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent =>
        _publishEndpoint.Publish(evento, typeof(T), cancellationToken);
}
