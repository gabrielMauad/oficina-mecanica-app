using MassTransit;
using OficinaMecanica.Contracts.IntegrationEvents;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace OrdensServico.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer MassTransit de <see cref="PagamentoAprovadoIntegrationEvent"/>: apenas delega ao handler
/// da Application. Em caso de falha registra a métrica de integração e relança a exceção, para o
/// retry do MassTransit atuar.
/// </summary>
public sealed class PagamentoAprovadoConsumer : IConsumer<PagamentoAprovadoIntegrationEvent>
{
    private readonly IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent> _handler;
    private readonly IntegracoesMetrics _metrics;

    public PagamentoAprovadoConsumer(
        IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent> handler,
        IntegracoesMetrics metrics)
    {
        _handler = handler;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<PagamentoAprovadoIntegrationEvent> context)
    {
        try
        {
            await _handler.Handle(context.Message, context.CancellationToken);
        }
        catch (Exception)
        {
            _metrics.RegistrarFalha(nameof(PagamentoAprovadoIntegrationEvent), _handler.GetType().Name);
            throw;
        }
    }
}
