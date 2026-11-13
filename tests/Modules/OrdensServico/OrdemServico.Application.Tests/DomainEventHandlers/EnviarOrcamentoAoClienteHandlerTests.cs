using Microsoft.Extensions.Logging.Abstractions;
using OrdensServico.Application.DomainEventHandlers;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace OrdensServico.Application.Tests.DomainEventHandlers;

public class EnviarOrcamentoAoClienteHandlerTests : IDisposable
{
    private readonly IntegracoesMetrics _metrics = new();
    private readonly Mock<IOrdemServicoGateway> _repoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<INotificacaoClienteGateway> _notificacaoMock = new();
    private readonly Mock<IClienteGateway> _clienteMock = new();
    private readonly Mock<IVeiculoGateway> _veiculoMock = new();
    private readonly Mock<IServicoGateway> _servicoMock = new();
    private readonly Mock<IPecaInsumoInfoGateway> _pecaInfoMock = new();
    private readonly EnviarOrcamentoAoCliente _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public EnviarOrcamentoAoClienteHandlerTests()
    {
        _handler = new(
            _repoMock.Object,
            _uowMock.Object,
            _notificacaoMock.Object,
            _clienteMock.Object,
            _veiculoMock.Object,
            _servicoMock.Object,
            _pecaInfoMock.Object,
            _metrics,
            NullLogger<EnviarOrcamentoAoCliente>.Instance);
    }

    public void Dispose() => _metrics.Dispose();

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsEmDiagnosticoComOrcamento()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico(
            "desc",
            [new ItemServicoInput(ServicoId, 1, 100m)],
            [new ItemPecaInput(PecaId, 1, 50m)]);
        return os;
    }

    private static OrcamentoGerado CriarNotificacao(OrdensServico.Domain.OrdemServico.OrdemServico os) =>
        os.DomainEvents.OfType<OrcamentoGerado>().Single();

    [Fact(DisplayName = "Cenário feliz: OS encontrada → EnviarOrcamento, Save e NotificarOrcamentoPronto chamados")]
    public async Task Handle_OsEncontrada_EnviaOrcamentoENotificaCliente()
    {
        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);

        _repoMock.Setup(x => x.ObterPorId(notification.OrdemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _uowMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clienteMock.Setup(x => x.ObterInfo(ClienteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClienteInfo("João Silva", "joao@email.com"));
        _veiculoMock.Setup(x => x.ObterPlaca(VeiculoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABC1D23");
        _servicoMock.Setup(x => x.ObterNome(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Troca de óleo");
        _pecaInfoMock.Setup(x => x.Obter(PecaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaInsumoInfo("Filtro", "un"));

        await _handler.Handle(notification, CancellationToken.None);

        _uowMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notificacaoMock.Verify(x => x.NotificarOrcamentoPronto(
            "João Silva",
            "joao@email.com",
            "ABC1D23",
            It.Is<IReadOnlyList<Application.Gateways.Dtos.ServicoEmailItem>>(l => l.Count == 1),
            It.Is<IReadOnlyList<Application.Gateways.Dtos.PecaEmailItem>>(l => l.Count == 1),
            notification.ValorTotal,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "OS não encontrada: retorna silenciosamente sem notificar")]
    public async Task Handle_OsNaoEncontrada_RetornaSemNotificar()
    {
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdensServico.Domain.OrdemServico.OrdemServico?)null);

        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);

        await _handler.Handle(notification, CancellationToken.None);

        _uowMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notificacaoMock.Verify(x => x.NotificarOrcamentoPronto(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.ServicoEmailItem>>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.PecaEmailItem>>(),
            It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "Falha ao consultar o Cadastro: não propaga, registra a métrica e o orçamento já foi salvo")]
    public async Task Handle_CadastroLancaExcecao_NaoPropagaERegistraMetrica()
    {
        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);

        _repoMock.Setup(x => x.ObterPorId(notification.OrdemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _clienteMock.Setup(x => x.ObterInfo(ClienteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Cadastro fora do ar"));
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await _handler.Handle(notification, CancellationToken.None);

        _uowMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
        var medicao = Assert.Single(falhas.GetMeasurementSnapshot());
        Assert.Equal(1, medicao.Value);
        Assert.Equal(nameof(OrcamentoGerado), medicao.Tags["evento"]);
        Assert.Equal(nameof(EnviarOrcamentoAoCliente), medicao.Tags["handler"]);
        _notificacaoMock.Verify(x => x.NotificarOrcamentoPronto(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.ServicoEmailItem>>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.PecaEmailItem>>(),
            It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "Falha ao enviar o e-mail: não propaga e registra a métrica")]
    public async Task Handle_EnvioDoEmailLancaExcecao_NaoPropagaERegistraMetrica()
    {
        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);

        _repoMock.Setup(x => x.ObterPorId(notification.OrdemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _clienteMock.Setup(x => x.ObterInfo(ClienteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClienteInfo("João Silva", "joao@email.com"));
        _veiculoMock.Setup(x => x.ObterPlaca(VeiculoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABC1D23");
        _servicoMock.Setup(x => x.ObterNome(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Troca de óleo");
        _pecaInfoMock.Setup(x => x.Obter(PecaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaInsumoInfo("Filtro", "un"));
        _notificacaoMock.Setup(x => x.NotificarOrcamentoPronto(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Application.Gateways.Dtos.ServicoEmailItem>>(),
                It.IsAny<IReadOnlyList<Application.Gateways.Dtos.PecaEmailItem>>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP indisponível"));
        using var falhas = new MetricCollector<long>(_metrics.Meter, IntegracoesMetrics.FalhasInstrumentName);

        await _handler.Handle(notification, CancellationToken.None);

        Assert.Single(falhas.GetMeasurementSnapshot());
    }

    [Fact(DisplayName = "Cancelamento da requisição durante o e-mail: a exceção é propagada")]
    public async Task Handle_RequisicaoCancelada_PropagaAExcecao()
    {
        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);
        using var cts = new CancellationTokenSource();

        _repoMock.Setup(x => x.ObterPorId(notification.OrdemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _clienteMock.Setup(x => x.ObterInfo(ClienteId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() => _handler.Handle(notification, cts.Token));
    }

    [Fact(DisplayName = "Cliente ou placa nulos: salva mas não notifica")]
    public async Task Handle_ClienteOuPlacaNull_SalvaMasNaoNotifica()
    {
        var os = CriarOsEmDiagnosticoComOrcamento();
        var notification = CriarNotificacao(os);

        _repoMock.Setup(x => x.ObterPorId(notification.OrdemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _uowMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clienteMock.Setup(x => x.ObterInfo(ClienteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClienteInfo?)null); // cliente não encontrado
        _veiculoMock.Setup(x => x.ObterPlaca(VeiculoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABC1D23");

        await _handler.Handle(notification, CancellationToken.None);

        _uowMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notificacaoMock.Verify(x => x.NotificarOrcamentoPronto(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.ServicoEmailItem>>(),
            It.IsAny<IReadOnlyList<Application.Gateways.Dtos.PecaEmailItem>>(),
            It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
