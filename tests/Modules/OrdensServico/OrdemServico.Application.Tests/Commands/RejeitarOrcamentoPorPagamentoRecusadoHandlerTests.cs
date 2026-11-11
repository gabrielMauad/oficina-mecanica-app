using Microsoft.Extensions.Logging.Abstractions;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;

namespace OrdensServico.Application.Tests.Commands;

public class RejeitarOrcamentoPorPagamentoRecusadoHandlerTests
{
    private readonly Mock<IOrdemServicoGateway> _repoMock = new();
    private readonly RejeitarOrcamentoPorPagamentoRecusadoHandler _handler;

    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    public RejeitarOrcamentoPorPagamentoRecusadoHandlerTests()
    {
        _handler = new(_repoMock.Object, NullLogger<RejeitarOrcamentoPorPagamentoRecusadoHandler>.Instance);
    }

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsAguardandoAprovacao()
    {
        var os = OrdensServico.Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico("desc", [new ItemServicoInput(ServicoId, 1, 100m)], [new ItemPecaInput(PecaId, 2, 50m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        return os;
    }

    private static OrdensServico.Domain.OrdemServico.OrdemServico CriarOsComOrcamentoAprovado()
    {
        var os = CriarOsAguardandoAprovacao();
        os.AprovarOrcamento();
        os.ClearDomainEvents();
        return os;
    }

    private void OsEncontrada(OrdensServico.Domain.OrdemServico.OrdemServico? os) =>
        _repoMock.Setup(x => x.ObterPorId(It.IsAny<OrdemServicoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(os);

    [Fact(DisplayName = "Cenário feliz: orçamento Aprovado → Rejeitado, persiste e registra OrcamentoRejeitado com as peças")]
    public async Task Handle_OrcamentoAprovado_RejeitaERegistraEvento()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(os.Id.Value, os.Orcamentos[0].Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusOrcamento.Rejeitado, result.Value.Orcamentos[0].Status);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, result.Value.Status);
        var evento = Assert.Single(os.DomainEvents.OfType<OrcamentoRejeitado>());
        Assert.Equal(PecaId, Assert.Single(evento.Pecas).PecaInsumoId);
        _repoMock.Verify(x => x.Atualizar(os, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Reentrega: orçamento já Rejeitado → sucesso sem persistir nem registrar novo estorno")]
    public async Task Handle_OrcamentoJaRejeitado_RetornaSucessoSemAlterar()
    {
        var os = CriarOsComOrcamentoAprovado();
        os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(os.Orcamentos[0].Id);
        os.ClearDomainEvents();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(os.Id.Value, os.Orcamentos[0].Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(os.DomainEvents);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro: OS não encontrada → NaoEncontrada")]
    public async Task Handle_OsNaoEncontrada_RetornaErro()
    {
        OsEncontrada(null);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.NaoEncontrada", result.Error.Code);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro: orçamento Enviado (nunca aprovado) → TransicaoInvalida, nada persistido")]
    public async Task Handle_OrcamentoEnviado_RetornaTransicaoInvalida()
    {
        var os = CriarOsAguardandoAprovacao();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(os.Id.Value, os.Orcamentos[0].Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Orcamento.TransicaoInvalida", result.Error.Code);
        Assert.Equal(StatusOrcamento.Enviado, os.Orcamentos[0].Status);
        _repoMock.Verify(x => x.Atualizar(It.IsAny<OrdensServico.Domain.OrdemServico.OrdemServico>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro: orçamento de outra OS → OrcamentoNaoEncontrado")]
    public async Task Handle_OrcamentoInexistente_RetornaErro()
    {
        var os = CriarOsComOrcamentoAprovado();
        OsEncontrada(os);

        var result = await _handler.Handle(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(os.Id.Value, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.OrcamentoNaoEncontrado", result.Error.Code);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }

    [Fact(DisplayName = "Validator: ids vazios são inválidos")]
    public void Validator_IdsVazios_SaoInvalidos()
    {
        var validator = new RejeitarOrcamentoPorPagamentoRecusadoValidator();

        var resultado = validator.Validate(new RejeitarOrcamentoPorPagamentoRecusadoCommand(Guid.Empty, Guid.Empty));

        Assert.False(resultado.IsValid);
        Assert.Equal(2, resultado.Errors.Count);
    }
}
