using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.DomainEventHandlers;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;

namespace OrdensServico.Application.Tests.DomainEventHandlers;

public class PublicarOrcamentoAprovadoHandlerTests
{
    private readonly Mock<IIntegrationEventBus> _busMock = new();
    private readonly PendingIntegrationEvents _pendingEvents = new();
    private readonly PublicarOrcamentoAprovado _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public PublicarOrcamentoAprovadoHandlerTests()
    {
        _handler = new(_busMock.Object, _pendingEvents);
    }

    private static OrcamentoAprovado CriarNotificacao()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico(
            "desc",
            [new ItemServicoInput(ServicoId, 1, 100m)],
            [new ItemPecaInput(PecaId, 3, 40m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        os.AprovarOrcamento();

        return os.DomainEvents.OfType<OrcamentoAprovado>().Single();
    }

    [Fact(DisplayName = "Handle: enfileira OrcamentoAprovadoIntegrationEvent com payload correto")]
    public async Task Handle_OrcamentoAprovado_EnfileiraEventoComPayloadCorreto()
    {
        var notification = CriarNotificacao();

        await _handler.Handle(notification, CancellationToken.None);

        // Captura os pendentes uma única vez — GetPending() drena a fila atomicamente
        var pending = _pendingEvents.GetPending();
        Assert.Single(pending);

        _busMock.Setup(x => x.Publish(It.IsAny<OrcamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await pending[0](CancellationToken.None);

        _busMock.Verify(x => x.Publish(
            It.Is<OrcamentoAprovadoIntegrationEvent>(e =>
                e.EventId != Guid.Empty &&
                e.OrdemServicoId == notification.OrdemServicoId.Value &&
                e.OrcamentoId == notification.OrcamentoId.Value &&
                e.ClienteId == ClienteId &&
                e.ValorTotal == 220m),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "Handle: não publica nada antes de o pipeline drenar os pendentes")]
    public async Task Handle_NaoPublicaDiretamente()
    {
        await _handler.Handle(CriarNotificacao(), CancellationToken.None);

        _busMock.Verify(x => x.Publish(It.IsAny<OrcamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
