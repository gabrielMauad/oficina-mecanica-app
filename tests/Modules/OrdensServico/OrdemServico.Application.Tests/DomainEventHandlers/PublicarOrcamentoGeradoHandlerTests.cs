using OrdensServico.Application.DomainEventHandlers;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;

namespace OrdensServico.Application.Tests.DomainEventHandlers;

public class PublicarOrcamentoGeradoHandlerTests
{
    private readonly Mock<IIntegrationEventBus> _busMock = new();
    private readonly PendingIntegrationEvents _pendingEvents = new();
    private readonly PublicarOrcamentoGerado _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public PublicarOrcamentoGeradoHandlerTests()
    {
        _handler = new(_busMock.Object, _pendingEvents);
    }

    private static OrcamentoGerado CriarNotificacao()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico(
            "desc",
            [new ItemServicoInput(ServicoId, 1, 100m)],
            [new ItemPecaInput(PecaId, 2, 50m)]);

        return os.DomainEvents.OfType<OrcamentoGerado>().Single();
    }

    [Fact(DisplayName = "Handle: enfileira OrcamentoGeradoIntegrationEvent com payload correto")]
    public async Task Handle_OrcamentoGerado_EnfileiradOrcamentoGeradoComPayloadCorreto()
    {
        var notification = CriarNotificacao();

        await _handler.Handle(notification, CancellationToken.None);

        // Captura os pendentes uma única vez — GetPending() drena a fila atomicamente
        var pending = _pendingEvents.GetPending();
        Assert.Single(pending);

        // Executar o evento e verificar que o bus foi chamado com o payload correto
        _busMock.Setup(x => x.Publish(It.IsAny<OrcamentoGeradoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await pending[0](CancellationToken.None);

        _busMock.Verify(x => x.Publish(
            It.Is<OrcamentoGeradoIntegrationEvent>(e =>
                e.OrdemServicoId == notification.OrdemServicoId.Value &&
                e.OrcamentoId == notification.OrcamentoId.Value &&
                e.Pecas.Count == 1 &&
                e.Pecas[0].PecaInsumoId == PecaId &&
                e.Pecas[0].Quantidade == 2),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
