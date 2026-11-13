using MediatR;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.IntegrationEventHandlers;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace OrdensServico.Application.Tests.IntegrationEventHandlers;

public class RejeitarOrcamentoQuandoEventosDaSagaTests : IDisposable
{
    private readonly IntegracoesMetrics _metrics = new();
    private readonly Mock<ISender> _senderMock = new();

    public void Dispose() => _metrics.Dispose();

    private static Result<OrdensServico.Domain.OrdemServico.OrdemServico> Falha() => Error.NotFound("OrdemServico.NaoEncontrada", "não encontrada");

    [Fact(DisplayName = "PagamentoRecusado: envia o comando de compensação com OS e orçamento do evento")]
    public async Task PagamentoRecusado_EnviaComandoDeCompensacao()
    {
        var handler = new RejeitarOrcamentoQuandoPagamentoRecusado(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoPagamentoRecusado>.Instance);
        var evento = new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cc_rejected_other_reason");
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorPagamentoRecusadoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());

        await handler.Handle(evento, CancellationToken.None);

        _senderMock.Verify(x => x.Send(
            It.Is<RejeitarOrcamentoPorPagamentoRecusadoCommand>(c =>
                c.OrdemServicoId == evento.OrdemServicoId && c.OrcamentoId == evento.OrcamentoId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PagamentoRecusado: falha de negócio é logada sem lançar exceção")]
    public async Task PagamentoRecusado_ComandoFalha_NaoLancaExcecao()
    {
        var handler = new RejeitarOrcamentoQuandoPagamentoRecusado(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoPagamentoRecusado>.Instance);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorPagamentoRecusadoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());

        var excecao = await Record.ExceptionAsync(() => handler.Handle(
            new PagamentoRecusadoIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "motivo"),
            CancellationToken.None));

        Assert.Null(excecao);
    }

    [Fact(DisplayName = "PagamentoRecusado: falha técnica é propagada para o retry do MassTransit")]
    public async Task PagamentoRecusado_FalhaTecnica_PropagaExcecao()
    {
        var handler = new RejeitarOrcamentoQuandoPagamentoRecusado(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoPagamentoRecusado>.Instance);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorPagamentoRecusadoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new PagamentoRecusadoIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "motivo"),
            CancellationToken.None));
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou: envia o comando com OS e orçamento do evento")]
    public async Task EstoqueBaixaFalhou_EnviaComando()
    {
        var handler = new RejeitarOrcamentoQuandoEstoqueBaixaFalhou(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou>.Instance);
        var evento = new EstoqueBaixaFalhouIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "estoque insuficiente");
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorFalhaDeEstoqueCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());

        await handler.Handle(evento, CancellationToken.None);

        _senderMock.Verify(x => x.Send(
            It.Is<RejeitarOrcamentoPorFalhaDeEstoqueCommand>(c =>
                c.OrdemServicoId == evento.OrdemServicoId && c.OrcamentoId == evento.OrcamentoId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou: falha de negócio é logada sem lançar exceção")]
    public async Task EstoqueBaixaFalhou_ComandoFalha_NaoLancaExcecao()
    {
        var handler = new RejeitarOrcamentoQuandoEstoqueBaixaFalhou(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou>.Instance);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorFalhaDeEstoqueCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());

        var excecao = await Record.ExceptionAsync(() => handler.Handle(
            new EstoqueBaixaFalhouIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "motivo"),
            CancellationToken.None));

        Assert.Null(excecao);
    }

    [Fact(DisplayName = "PagamentoRecusado: falha de negócio registra a métrica de falha de integração")]
    public async Task PagamentoRecusado_ComandoFalha_RegistraMetrica()
    {
        var handler = new RejeitarOrcamentoQuandoPagamentoRecusado(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoPagamentoRecusado>.Instance);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorPagamentoRecusadoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await handler.Handle(
            new PagamentoRecusadoIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "motivo"),
            CancellationToken.None);

        var medicao = Assert.Single(falhas.GetMeasurementSnapshot());
        Assert.Equal(nameof(PagamentoRecusadoIntegrationEvent), medicao.Tags["evento"]);
        Assert.Equal(nameof(RejeitarOrcamentoQuandoPagamentoRecusado), medicao.Tags["handler"]);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou: falha de negócio registra a métrica de falha de integração")]
    public async Task EstoqueBaixaFalhou_ComandoFalha_RegistraMetrica()
    {
        var handler = new RejeitarOrcamentoQuandoEstoqueBaixaFalhou(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou>.Instance);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorFalhaDeEstoqueCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Falha());
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await handler.Handle(
            new EstoqueBaixaFalhouIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "motivo"),
            CancellationToken.None);

        var medicao = Assert.Single(falhas.GetMeasurementSnapshot());
        Assert.Equal(nameof(EstoqueBaixaFalhouIntegrationEvent), medicao.Tags["evento"]);
        Assert.Equal(nameof(RejeitarOrcamentoQuandoEstoqueBaixaFalhou), medicao.Tags["handler"]);
    }

    [Fact(DisplayName = "PagamentoRecusado e EstoqueBaixaFalhou: comando com sucesso não registra métrica")]
    public async Task ComandoComSucesso_NaoRegistraMetrica()
    {
        Result<OrdensServico.Domain.OrdemServico.OrdemServico> sucesso =
            OrdensServico.Domain.OrdemServico.OrdemServico.Criar(Guid.NewGuid(), Guid.NewGuid()).Value;
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorPagamentoRecusadoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sucesso);
        _senderMock.Setup(x => x.Send(It.IsAny<RejeitarOrcamentoPorFalhaDeEstoqueCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sucesso);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await new RejeitarOrcamentoQuandoPagamentoRecusado(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoPagamentoRecusado>.Instance)
            .Handle(new PagamentoRecusadoIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "m"), CancellationToken.None);
        await new RejeitarOrcamentoQuandoEstoqueBaixaFalhou(
            _senderMock.Object, _metrics, NullLogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou>.Instance)
            .Handle(new EstoqueBaixaFalhouIntegrationEvent(
                Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "m"), CancellationToken.None);

        Assert.Empty(falhas.GetMeasurementSnapshot());
    }
}
