using Microsoft.Extensions.Logging.Abstractions;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;
using OrdensServico.Domain.OrdemServico;

namespace OrdensServico.Application.Tests.Commands;

public class RejeitarOrcamentoPorFalhaDeEstoqueHandlerTests
{
    private readonly Mock<IOrdemServicoGateway> _repoMock = new();
    private readonly RejeitarOrcamentoPorFalhaDeEstoqueHandler _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public RejeitarOrcamentoPorFalhaDeEstoqueHandlerTests()
    {
        _handler = new(_repoMock.Object, NullLogger<RejeitarOrcamentoPorFalhaDeEstoqueHandler>.Instance);
    }

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsAguardandoAprovacao()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico("desc", [new ItemServicoInput(ServicoId, 1, 100m)], [new ItemPecaInput(PecaId, 2, 50m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        os.ClearDomainEvents();
        return os;
    }

    private void OsEncontrada(OrdensServico.Domain.OrdemServico.OrdemServico? os) =>
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);

    [Fact(DisplayName = "Cenário feliz: orçamento Enviado → Rejeitado, persiste e NÃO registra OrcamentoRejeitado")]
    public async Task Handle_OrcamentoEnviado_RejeitaSemEvento()
    {
        var os = CriarOsAguardandoAprovacao();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(os.Id.Value, os.Orcamentos[0].Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusOrcamento.Rejeitado, result.Value.Orcamentos[0].Status);
        Assert.Empty(os.DomainEvents);
        _repoMock.Verify(x => x.Atualizar(os, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Reentrega: orçamento já Rejeitado → sucesso sem persistir")]
    public async Task Handle_OrcamentoJaRejeitado_RetornaSucessoSemAlterar()
    {
        var os = CriarOsAguardandoAprovacao();
        os.RejeitarOrcamento();
        os.ClearDomainEvents();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(os.Id.Value, os.Orcamentos[0].Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro: OS não encontrada → NaoEncontrada")]
    public async Task Handle_OsNaoEncontrada_RetornaErro()
    {
        OsEncontrada(null);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.NaoEncontrada", result.Error.Code);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro: orçamento de outra OS → OrcamentoNaoEncontrado")]
    public async Task Handle_OrcamentoInexistente_RetornaErro()
    {
        var os = CriarOsAguardandoAprovacao();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(os.Id.Value, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.OrcamentoNaoEncontrado", result.Error.Code);
        Assert.Equal(StatusOrcamento.Enviado, os.Orcamentos[0].Status);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Validator: ids vazios são inválidos")]
    public void Validator_IdsVazios_SaoInvalidos()
    {
        var validator = new RejeitarOrcamentoPorFalhaDeEstoqueValidator();

        var resultado = validator.Validate(new RejeitarOrcamentoPorFalhaDeEstoqueCommand(Guid.Empty, Guid.Empty));

        Assert.False(resultado.IsValid);
        Assert.Equal(2, resultado.Errors.Count);
    }
}
