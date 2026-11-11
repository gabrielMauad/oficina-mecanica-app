using System.Diagnostics.Metrics;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Infrastructure.Messaging.Consumers;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace OrdemServico.Infrastructure.Tests.Messaging;

public class SagaConsumersTests
{
    private static PagamentoAprovadoIntegrationEvent NovoPagamentoAprovado() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static PagamentoRecusadoIntegrationEvent NovoPagamentoRecusado() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "recusado");

    private static EstoqueBaixaFalhouIntegrationEvent NovaFalhaDeEstoque() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "estoque insuficiente");

    private static ServiceProvider BuildProvider(
        Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>> aprovado,
        Mock<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>> recusado,
        Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>> estoque)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IntegracoesMetrics>();
        services.AddScoped(_ => aprovado.Object);
        services.AddScoped(_ => recusado.Object);
        services.AddScoped(_ => estoque.Object);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<PagamentoAprovadoConsumer>();
            x.AddConsumer<PagamentoRecusadoConsumer>();
            x.AddConsumer<EstoqueBaixaFalhouConsumer>();
        });
        return services.BuildServiceProvider(true);
    }

    [Fact(DisplayName = "PagamentoAprovado — consumer delega ao handler da Application com a mensagem recebida")]
    public async Task PagamentoAprovado_DeveDelegarAoHandler()
    {
        var aprovado = new Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>>();
        var recusado = new Mock<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>>();
        var estoque = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        await using var provider = BuildProvider(aprovado, recusado, estoque);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var evento = NovoPagamentoAprovado();

        await harness.Bus.Publish(evento);

        Assert.True(await harness.GetConsumerHarness<PagamentoAprovadoConsumer>()
            .Consumed.Any<PagamentoAprovadoIntegrationEvent>());
        aprovado.Verify(h => h.Handle(
            It.Is<PagamentoAprovadoIntegrationEvent>(e => e.EventId == evento.EventId),
            It.IsAny<CancellationToken>()), Times.Once);
        recusado.Verify(h => h.Handle(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        estoque.Verify(h => h.Handle(It.IsAny<EstoqueBaixaFalhouIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "PagamentoRecusado — consumer delega ao handler da Application com a mensagem recebida")]
    public async Task PagamentoRecusado_DeveDelegarAoHandler()
    {
        var aprovado = new Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>>();
        var recusado = new Mock<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>>();
        var estoque = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        await using var provider = BuildProvider(aprovado, recusado, estoque);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var evento = NovoPagamentoRecusado();

        await harness.Bus.Publish(evento);

        Assert.True(await harness.GetConsumerHarness<PagamentoRecusadoConsumer>()
            .Consumed.Any<PagamentoRecusadoIntegrationEvent>());
        recusado.Verify(h => h.Handle(
            It.Is<PagamentoRecusadoIntegrationEvent>(e => e.EventId == evento.EventId && e.Motivo == "recusado"),
            It.IsAny<CancellationToken>()), Times.Once);
        aprovado.Verify(h => h.Handle(It.IsAny<PagamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou — consumer delega ao handler da Application com a mensagem recebida")]
    public async Task EstoqueBaixaFalhou_DeveDelegarAoHandler()
    {
        var aprovado = new Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>>();
        var recusado = new Mock<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>>();
        var estoque = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        await using var provider = BuildProvider(aprovado, recusado, estoque);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var evento = NovaFalhaDeEstoque();

        await harness.Bus.Publish(evento);

        Assert.True(await harness.GetConsumerHarness<EstoqueBaixaFalhouConsumer>()
            .Consumed.Any<EstoqueBaixaFalhouIntegrationEvent>());
        estoque.Verify(h => h.Handle(
            It.Is<EstoqueBaixaFalhouIntegrationEvent>(e => e.EventId == evento.EventId && e.Motivo == "estoque insuficiente"),
            It.IsAny<CancellationToken>()), Times.Once);
        recusado.Verify(h => h.Handle(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<ConsumeContext<T>> ContextoDe<T>(T mensagem) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(mensagem);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static List<(string Evento, string Handler)> ColetarFalhas(IntegracoesMetrics metrics, out MeterListener listener)
    {
        var falhas = new List<(string, string)>();
        listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == metrics.Meter) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string evento = string.Empty, handler = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "evento") evento = tag.Value?.ToString() ?? string.Empty;
                if (tag.Key == "handler") handler = tag.Value?.ToString() ?? string.Empty;
            }
            falhas.Add((evento, handler));
        });
        listener.Start();
        return falhas;
    }

    [Fact(DisplayName = "PagamentoAprovado — falha do handler é propagada (retry) e registrada na métrica")]
    public async Task PagamentoAprovado_PropagaFalhaERegistraMetrica()
    {
        var handler = new Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>>();
        handler.Setup(h => h.Handle(It.IsAny<PagamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha técnica"));
        using var metrics = new IntegracoesMetrics();
        var falhas = ColetarFalhas(metrics, out var listener);
        using var _ = listener;
        var consumer = new PagamentoAprovadoConsumer(handler.Object, metrics);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.Consume(ContextoDe(NovoPagamentoAprovado()).Object));

        Assert.Equal("falha técnica", ex.Message);
        var falha = Assert.Single(falhas);
        Assert.Equal(nameof(PagamentoAprovadoIntegrationEvent), falha.Evento);
    }

    [Fact(DisplayName = "PagamentoRecusado — falha do handler é propagada (retry) e registrada na métrica")]
    public async Task PagamentoRecusado_PropagaFalhaERegistraMetrica()
    {
        var handler = new Mock<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>>();
        handler.Setup(h => h.Handle(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha técnica"));
        using var metrics = new IntegracoesMetrics();
        var falhas = ColetarFalhas(metrics, out var listener);
        using var _ = listener;
        var consumer = new PagamentoRecusadoConsumer(handler.Object, metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.Consume(ContextoDe(NovoPagamentoRecusado()).Object));

        var falha = Assert.Single(falhas);
        Assert.Equal(nameof(PagamentoRecusadoIntegrationEvent), falha.Evento);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou — falha do handler é propagada (retry) e registrada na métrica")]
    public async Task EstoqueBaixaFalhou_PropagaFalhaERegistraMetrica()
    {
        var handler = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        handler.Setup(h => h.Handle(It.IsAny<EstoqueBaixaFalhouIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha técnica"));
        using var metrics = new IntegracoesMetrics();
        var falhas = ColetarFalhas(metrics, out var listener);
        using var _ = listener;
        var consumer = new EstoqueBaixaFalhouConsumer(handler.Object, metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.Consume(ContextoDe(NovaFalhaDeEstoque()).Object));

        var falha = Assert.Single(falhas);
        Assert.Equal(nameof(EstoqueBaixaFalhouIntegrationEvent), falha.Evento);
    }

    [Fact(DisplayName = "Consumers com sucesso não registram falha na métrica")]
    public async Task Consumers_ComSucesso_NaoRegistramFalha()
    {
        var handler = new Mock<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>>();
        using var metrics = new IntegracoesMetrics();
        var falhas = ColetarFalhas(metrics, out var listener);
        using var _ = listener;
        var consumer = new PagamentoAprovadoConsumer(handler.Object, metrics);

        await consumer.Consume(ContextoDe(NovoPagamentoAprovado()).Object);

        Assert.Empty(falhas);
    }
}
