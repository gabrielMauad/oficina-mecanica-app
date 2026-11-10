using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Infrastructure.Messaging;
using SharedKernel.Application;
using SharedKernel.Domain;

namespace OrdemServico.Infrastructure.Tests.Messaging;

public class MensageriaTests
{
    private static IConfiguration Config(string transporte) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mensageria:Transporte"] = transporte })
            .Build();

    private static ServiceCollection CriarServices(string transporte)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelServices();
        services.AddMensageria(Config(transporte));
        return services;
    }

    [Theory]
    [InlineData(MensageriaOptions.TransporteInMemory)]
    [InlineData(MensageriaOptions.TransporteAmazonSqs)]
    public async Task IIntegrationEventBus_ResolvidoDoContainer_ESempreOAdapterMassTransit(string transporte)
    {
        await using var provider = CriarServices(transporte).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();

        Assert.IsType<MassTransitIntegrationEventBus>(bus);
    }

    [Fact]
    public void Transporte_Invalido_LancaInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CriarServices("Kafka"));

        Assert.Contains("Mensageria:Transporte", ex.Message);
    }

    [Fact]
    public async Task Publish_OrcamentoGerado_PublicaOEventoComOMesmoConteudo()
    {
        await using var provider = CriarServices(MensageriaOptions.TransporteInMemory)
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evento = new OrcamentoGeradoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            [new ItemPecaEventDto(Guid.NewGuid(), 3)]);

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();
            await bus.Publish(evento);
        }

        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>());
        var publicado = harness.Published.Select<OrcamentoGeradoIntegrationEvent>().Single().Context.Message;
        Assert.Equal(evento.OrdemServicoId, publicado.OrdemServicoId);
        Assert.Equal(evento.OrcamentoId, publicado.OrcamentoId);
        Assert.Equal(evento.Pecas, publicado.Pecas);
    }

    [Fact]
    public async Task Publish_OrcamentoRejeitado_PublicaOEventoComOTipoConcreto()
    {
        await using var provider = CriarServices(MensageriaOptions.TransporteInMemory)
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evento = new OrcamentoRejeitadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            [new ItemPecaEventDto(Guid.NewGuid(), 1)]);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>().Publish(evento);

        Assert.True(await harness.Published.Any<OrcamentoRejeitadoIntegrationEvent>());
        Assert.False(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>());
    }

    [Fact]
    public async Task Publish_ViaTipoAbstrato_PublicaComOTipoConcretoEmExecucao()
    {
        await using var provider = CriarServices(MensageriaOptions.TransporteInMemory)
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        IIntegrationEvent evento = new OrcamentoGeradoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            [new ItemPecaEventDto(Guid.NewGuid(), 2)]);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>().Publish(evento);

        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>());
        var publicado = harness.Published.Select<OrcamentoGeradoIntegrationEvent>().Single();
        Assert.Equal(evento.EventId, publicado.Context.Message.EventId);
        Assert.Contains(
            "urn:message:OficinaMecanica.Contracts.IntegrationEvents:OrcamentoGeradoIntegrationEvent",
            publicado.Context.SupportedMessageTypes);
    }
}
