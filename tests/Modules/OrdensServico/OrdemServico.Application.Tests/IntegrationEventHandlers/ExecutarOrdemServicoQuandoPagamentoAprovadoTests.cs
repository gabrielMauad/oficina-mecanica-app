using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.IntegrationEventHandlers;
using OrdensServico.Application.Ordens.Commands.ExecutarOrdemServico;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Tests.IntegrationEventHandlers;

public class ExecutarOrdemServicoQuandoPagamentoAprovadoTests
{
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<IOrdemServicoGateway> _repoMock = new();
    private readonly ExecutarOrdemServicoQuandoPagamentoAprovado _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public ExecutarOrdemServicoQuandoPagamentoAprovadoTests()
    {
        _handler = new(
            _senderMock.Object,
            _repoMock.Object,
            NullLogger<ExecutarOrdemServicoQuandoPagamentoAprovado>.Instance);
    }

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsComOrcamentoAprovado()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico("desc", [new ItemServicoInput(ServicoId, 1, 100m)], [new ItemPecaInput(PecaId, 1, 50m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        os.AprovarOrcamento();
        return os;
    }

    private static PagamentoAprovadoIntegrationEvent Evento(Guid ordemServicoId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), ordemServicoId, Guid.NewGuid());

    private void OsEncontrada(OrdensServico.Domain.OrdemServico.OrdemServico? os) =>
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);

    [Fact(DisplayName = "Cenário feliz: OS Aguardando Aprovação → envia ExecutarOrdemServicoCommand")]
    public async Task Handle_OsAguardandoAprovacao_EnviaComandoDeExecucao()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);
        Result<OrdensServico.Domain.OrdemServico.OrdemServico> sucesso = os;
        _senderMock.Setup(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sucesso);

        await _handler.Handle(Evento(os.Id.Value), CancellationToken.None);

        _senderMock.Verify(x => x.Send(
            It.Is<ExecutarOrdemServicoCommand>(c => c.OrdemServicoId == os.Id.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory(DisplayName = "Reentrega: OS já em execução ou além → log e retorno sem comando")]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    public async Task Handle_OsJaExecutada_IgnoraSemErro(StatusOrdemServico status)
    {
        var os = CriarOsComOrcamentoAprovado();
        os.Executar();
        if (status >= StatusOrdemServico.Finalizada) os.Finalizar();
        if (status == StatusOrdemServico.Entregue)
        {
            os.NotificarCliente(DateTime.UtcNow);
            os.Concluir(DateTime.UtcNow);
        }
        Assert.Equal(status, os.Status);
        OsEncontrada(os);

        await _handler.Handle(Evento(os.Id.Value), CancellationToken.None);

        _senderMock.Verify(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "OS não encontrada: log e retorno sem comando e sem exceção")]
    public async Task Handle_OsNaoEncontrada_IgnoraSemErro()
    {
        OsEncontrada(null);

        await _handler.Handle(Evento(Guid.NewGuid()), CancellationToken.None);

        _senderMock.Verify(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Falha de negócio no comando: registrada em log, sem exceção (retry não resolveria)")]
    public async Task Handle_ComandoFalha_NaoLancaExcecao()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);
        Result<OrdensServico.Domain.OrdemServico.OrdemServico> falha = Error.Validation("OrdemServico.TransicaoInvalida", "inválida");
        _senderMock.Setup(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(falha);

        var excecao = await Record.ExceptionAsync(() => _handler.Handle(Evento(os.Id.Value), CancellationToken.None));

        Assert.Null(excecao);
    }

    [Fact(DisplayName = "Falha técnica ao consultar a OS: a exceção é propagada para o retry do MassTransit")]
    public async Task Handle_FalhaTecnica_PropagaExcecao()
    {
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(Evento(Guid.NewGuid()), CancellationToken.None));
    }
}
