using OrdensServico.Application.DomainEventHandlers;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;

namespace OrdensServico.Application.Tests.DomainEventHandlers;

public class PublicarOrcamentoRejeitadoHandlerTests
{
    private readonly Mock<IIntegrationEventBus> _busMock = new();
    private readonly PendingIntegrationEvents _pendingEvents = new();
    private readonly PublicarOrcamentoRejeitado _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public PublicarOrcamentoRejeitadoHandlerTests()
    {
        _handler = new(_busMock.Object, _pendingEvents);
    }

    private static OrcamentoRejeitado CriarNotificacao()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico(
            "desc",
            [new ItemServicoInput(ServicoId, 1, 100m)],
            [new ItemPecaInput(PecaId, 3, 40m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        os.RejeitarOrcamento();

        return os.DomainEvents.OfType<OrcamentoRejeitado>().Single();
    }

    [Fact(DisplayName = "Handle: enfileira OrcamentoRejeitadoIntegrationEvent com payload correto")]
    public async Task Handle_OrcamentoRejeitado_EnfileiradOrcamentoRejeitadoComPayloadCorreto()
    {
        var notification = CriarNotificacao();

        await _handler.Handle(notification, CancellationToken.None);

        // Captura os pendentes uma única vez — GetPending() drena a fila atomicamente
        var pending = _pendingEvents.GetPending();
        Assert.Single(pending);

        // Executar o evento e verificar que o bus foi chamado com o payload correto
        _busMock.Setup(x => x.Publish(It.IsAny<OrcamentoRejeitadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await pending[0](CancellationToken.None);

        _busMock.Verify(x => x.Publish(
            It.Is<OrcamentoRejeitadoIntegrationEvent>(e =>
                e.OrdemServicoId == notification.OrdemServicoId.Value &&
                e.OrcamentoId == notification.OrcamentoId.Value &&
                e.Pecas.Count == 1 &&
                e.Pecas[0].PecaInsumoId == PecaId &&
                e.Pecas[0].Quantidade == 3),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
