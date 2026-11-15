using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;
using OrdensServico.Application.Metrics;
using OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Tests.Commands;

public class RegistrarDiagnosticoHandlerTests
{
    private readonly Mock<IServicoGateway> _servicoMock = new();
    private readonly Mock<IPecaDisponibilidadeGateway> _pecaMock = new();
    private readonly Mock<IOrdemServicoGateway> _repoMock = new();
    private readonly OrdensServicoMetrics _metrics = new();
    private readonly RegistrarDiagnosticoHandler _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public RegistrarDiagnosticoHandlerTests()
    {
        _handler = new(_servicoMock.Object, _pecaMock.Object, _repoMock.Object, _metrics);
        _servicoMock.Setup(x => x.EstaAtivo(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsEmDiagnostico()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        return os;
    }

    [Fact(DisplayName = "Cenário feliz: serviço e peça disponíveis → registra diagnóstico e retorna DTO")]
    public async Task Handle_ServicoEPecaDisponiveis_RegistraDiagnostico()
    {
        using var collector = new MetricCollector<double>(_metrics.Meter, OrdensServicoMetrics.EtapaDuracaoInstrumentName);
        var os = CriarOsEmDiagnostico();
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _servicoMock.Setup(x => x.ObterPreco(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);
        _pecaMock.Setup(x => x.Verificar(PecaId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaDisponibilidade(true, 50m));

        var command = new RegistrarDiagnosticoCommand(
            os.Id.Value,
            "Diagnóstico de teste",
            [new ServicoItem(ServicoId, 1)],
            [new PecaItem(PecaId, 2)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ItensServico);
        Assert.Single(result.Value.ItensPeca);
        Assert.Single(result.Value.Orcamentos);
        Assert.Equal("Pendente", result.Value.Orcamentos[0].Status.ToString());
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Once);

        var medicao = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.True(medicao.Value >= 0);
        Assert.Equal("diagnostico", medicao.Tags["etapa"]);
    }

    [Fact(DisplayName = "Erro: OS não encontrada → NaoEncontrada")]
    public async Task Handle_OsNaoEncontrada_RetornaErroNaoEncontrada()
    {
        using var collector = new MetricCollector<double>(_metrics.Meter, OrdensServicoMetrics.EtapaDuracaoInstrumentName);
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdensServico.Domain.OrdemServico.OrdemServico?)null);

        var command = new RegistrarDiagnosticoCommand(
            Guid.NewGuid(), "desc", [new ServicoItem(ServicoId, 1)], [new PecaItem(PecaId, 1)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.NaoEncontrada", result.Error.Code);
        Assert.Empty(collector.GetMeasurementSnapshot());
    }

    [Fact(DisplayName = "Erro: IServicoGateway retorna null → ServicoNaoEncontrado")]
    public async Task Handle_ServicoNaoEncontrado_RetornaErroServicoNaoEncontrado()
    {
        var os = CriarOsEmDiagnostico();
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _servicoMock.Setup(x => x.ObterPreco(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null); // serviço não encontrado
        _pecaMock.Setup(x => x.Verificar(PecaId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaDisponibilidade(true, 50m));

        var command = new RegistrarDiagnosticoCommand(
            os.Id.Value, "desc", [new ServicoItem(ServicoId, 1)], [new PecaItem(PecaId, 1)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.ServicoNaoEncontrado", result.Error.Code);
    }

    [Fact(DisplayName = "Erro: peça não encontrada (Verificar retorna null) → PecaNaoEncontrada")]
    public async Task Handle_PecaNaoEncontrada_RetornaErroPecaNaoEncontrada()
    {
        var os = CriarOsEmDiagnostico();
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _servicoMock.Setup(x => x.ObterPreco(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);
        _pecaMock.Setup(x => x.Verificar(PecaId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PecaDisponibilidade?)null); // peça não cadastrada

        var command = new RegistrarDiagnosticoCommand(
            os.Id.Value, "desc", [new ServicoItem(ServicoId, 1)], [new PecaItem(PecaId, 1)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.PecaNaoEncontrada", result.Error.Code);
    }

    [Fact(DisplayName = "Erro: peça sem estoque suficiente → PecaIndisponivel")]
    public async Task Handle_PecaIndisponivel_RetornaErroPecaIndisponivel()
    {
        var os = CriarOsEmDiagnostico();
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _servicoMock.Setup(x => x.ObterPreco(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);
        _pecaMock.Setup(x => x.Verificar(PecaId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaDisponibilidade(false, 50m)); // disponível mas sem estoque suficiente

        var command = new RegistrarDiagnosticoCommand(
            os.Id.Value, "desc", [new ServicoItem(ServicoId, 1)], [new PecaItem(PecaId, 5)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.PecaIndisponivel", result.Error.Code);
    }

    [Fact(DisplayName = "Erro: serviço desativado no Cadastro → ServicoDesativado")]
    public async Task Handle_ServicoDesativado_RetornaErroServicoDesativado()
    {
        var os = CriarOsEmDiagnostico();
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);
        _servicoMock.Setup(x => x.ObterPreco(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);
        _servicoMock.Setup(x => x.EstaAtivo(ServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _pecaMock.Setup(x => x.Verificar(PecaId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PecaDisponibilidade(true, 50m));

        var command = new RegistrarDiagnosticoCommand(
            os.Id.Value, "desc", [new ServicoItem(ServicoId, 1)], [new PecaItem(PecaId, 1)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.ServicoDesativado", result.Error.Code);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
