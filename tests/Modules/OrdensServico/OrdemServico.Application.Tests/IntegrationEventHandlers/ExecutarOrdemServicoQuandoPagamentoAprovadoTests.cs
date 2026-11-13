using MediatR;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.IntegrationEventHandlers;
using OrdensServico.Application.Ordens.Commands.ExecutarOrdemServico;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace OrdensServico.Application.Tests.IntegrationEventHandlers;

public class ExecutarOrdemServicoQuandoPagamentoAprovadoTests : IDisposable
{
    private readonly IntegracoesMetrics _metrics = new();
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
            _metrics,
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

    private static PagamentoAprovadoIntegrationEvent EventoDoOrcamento(OrdensServico.Domain.OrdemServico.OrdemServico os, Guid orcamentoId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), os.Id.Value, orcamentoId);

    public void Dispose() => _metrics.Dispose();

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

        await _handler.Handle(EventoDoOrcamento(os, os.Orcamentos.Single(x => x.Status == StatusOrcamento.Aprovado).Id.Value), CancellationToken.None);

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

        await _handler.Handle(EventoDoOrcamento(os, os.Orcamentos.Single(x => x.Status == StatusOrcamento.Aprovado).Id.Value), CancellationToken.None);

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

        var excecao = await Record.ExceptionAsync(() => _handler.Handle(EventoDoOrcamento(os, os.Orcamentos.Single(x => x.Status == StatusOrcamento.Aprovado).Id.Value), CancellationToken.None));

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

    [Fact(DisplayName = "Pagamento de orçamento Rejeitado (mesmo com outro Aprovado na OS): não executa, loga e registra a métrica")]
    public async Task Handle_OrcamentoDoEventoRejeitado_NaoExecutaERegistraMetrica()
    {
        // Orçamento X aprovado e depois rejeitado (compensação); novo diagnóstico gera Y, que é aprovado.
        var os = CriarOsComOrcamentoAprovado();
        var orcamentoRejeitadoId = os.Orcamentos.Single().Id;
        Assert.True(os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(orcamentoRejeitadoId).IsSuccess);
        Assert.True(os.RegistrarDiagnostico("novo", [new ItemServicoInput(ServicoId, 1, 100m)], [new ItemPecaInput(PecaId, 1, 50m)]).IsSuccess);
        Assert.True(os.EnviarOrcamento(DateTime.UtcNow).IsSuccess);
        Assert.True(os.AprovarOrcamento().IsSuccess);
        Assert.Contains(os.Orcamentos, x => x.Status == StatusOrcamento.Aprovado);
        OsEncontrada(os);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        var excecao = await Record.ExceptionAsync(() =>
            _handler.Handle(EventoDoOrcamento(os, orcamentoRejeitadoId.Value), CancellationToken.None));

        Assert.Null(excecao);
        _senderMock.Verify(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertFalhaRegistrada(falhas);
    }

    [Fact(DisplayName = "Pagamento de orçamento que não existe na OS: não executa, loga e registra a métrica")]
    public async Task Handle_OrcamentoDoEventoInexistente_NaoExecutaERegistraMetrica()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        var excecao = await Record.ExceptionAsync(() =>
            _handler.Handle(EventoDoOrcamento(os, Guid.NewGuid()), CancellationToken.None));

        Assert.Null(excecao);
        _senderMock.Verify(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertFalhaRegistrada(falhas);
    }

    [Fact(DisplayName = "Falha de negócio no comando: registra a métrica de falha de integração")]
    public async Task Handle_ComandoFalha_RegistraMetrica()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);
        Result<OrdensServico.Domain.OrdemServico.OrdemServico> falha = Error.Validation("OrdemServico.TransicaoInvalida", "inválida");
        _senderMock.Setup(x => x.Send(It.IsAny<ExecutarOrdemServicoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(falha);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await _handler.Handle(
            EventoDoOrcamento(os, os.Orcamentos.Single().Id.Value), CancellationToken.None);

        AssertFalhaRegistrada(falhas);
    }

    [Fact(DisplayName = "OS não encontrada: registra a métrica de falha de integração")]
    public async Task Handle_OsNaoEncontrada_RegistraMetrica()
    {
        OsEncontrada(null);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await _handler.Handle(Evento(Guid.NewGuid()), CancellationToken.None);

        AssertFalhaRegistrada(falhas);
    }

    [Fact(DisplayName = "Reentrega com a OS já em execução: não registra métrica de falha")]
    public async Task Handle_Reentrega_NaoRegistraMetrica()
    {
        var os = CriarOsComOrcamentoAprovado();
        os.Executar();
        OsEncontrada(os);
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await _handler.Handle(EventoDoOrcamento(os, os.Orcamentos.Single().Id.Value), CancellationToken.None);

        Assert.Empty(falhas.GetMeasurementSnapshot());
    }

    private static void AssertFalhaRegistrada(MetricCollector<long> falhas)
    {
        var medicao = Assert.Single(falhas.GetMeasurementSnapshot());
        Assert.Equal(1, medicao.Value);
        Assert.Equal(nameof(PagamentoAprovadoIntegrationEvent), medicao.Tags["evento"]);
        Assert.Equal(nameof(ExecutarOrdemServicoQuandoPagamentoAprovado), medicao.Tags["handler"]);
    }
}
