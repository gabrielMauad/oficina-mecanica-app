using MassTransit;
using OficinaMecanica.Contracts.IntegrationEvents;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace OrdensServico.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer MassTransit de <see cref="PagamentoRecusadoIntegrationEvent"/>: apenas delega ao handler
/// da Application (compensação da Saga). Em caso de falha registra a métrica de integração e
/// relança a exceção, para o retry do MassTransit atuar.
/// </summary>
public sealed class PagamentoRecusadoConsumer : IConsumer<PagamentoRecusadoIntegrationEvent>
{
    private readonly IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent> _handler;
    private readonly IntegracoesMetrics _metrics;

    public PagamentoRecusadoConsumer(
        IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent> handler,
        IntegracoesMetrics metrics)
    {
        _handler = handler;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<PagamentoRecusadoIntegrationEvent> context)
    {
        try
        {
            await _handler.Handle(context.Message, context.CancellationToken);
        }
        catch (Exception)
        {
            _metrics.RegistrarFalha(nameof(PagamentoRecusadoIntegrationEvent), _handler.GetType().Name);
            throw;
        }
    }
}
