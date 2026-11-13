using MassTransit;
using OficinaMecanica.Contracts.IntegrationEvents;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace OrdensServico.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer MassTransit de <see cref="EstoqueBaixaFalhouIntegrationEvent"/>: apenas delega ao handler
/// da Application. Em caso de falha registra a métrica de integração e relança a exceção, para o
/// retry do MassTransit atuar.
/// </summary>
public sealed class EstoqueBaixaFalhouConsumer : IConsumer<EstoqueBaixaFalhouIntegrationEvent>
{
    private readonly IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent> _handler;
    private readonly IntegracoesMetrics _metrics;

    public EstoqueBaixaFalhouConsumer(
        IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent> handler,
        IntegracoesMetrics metrics)
    {
        _handler = handler;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<EstoqueBaixaFalhouIntegrationEvent> context)
    {
        try
        {
            await _handler.Handle(context.Message, context.CancellationToken);
        }
        catch (Exception)
        {
            _metrics.RegistrarFalha(nameof(EstoqueBaixaFalhouIntegrationEvent), _handler.GetType().Name);
            throw;
        }
    }
}
