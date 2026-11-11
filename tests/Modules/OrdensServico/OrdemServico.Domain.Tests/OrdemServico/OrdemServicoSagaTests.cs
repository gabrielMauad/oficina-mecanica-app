using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;

namespace OrdensServico.Domain.Tests.OrdemServico;

/// <summary>
/// Transições do domínio acionadas pela Saga: aprovação (publica o evento), compensação do
/// pagamento recusado e falha na baixa de estoque.
/// </summary>
public class OrdemServicoSagaTests
{
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid VeiculoId = Guid.NewGuid();
    private static readonly Guid ServicoId = Guid.NewGuid();
    private static readonly Guid PecaId = Guid.NewGuid();

    private static Domain.OrdemServico.OrdemServico CriarOsAguardandoAprovacao()
    {
        var os = Domain.OrdemServico.OrdemServico.Criar(ClienteId, VeiculoId).Value;
        os.IniciarDiagnostico();
        os.RegistrarDiagnostico(
            "desc",
            [new ItemServicoInput(ServicoId, 2, 100m)],
            [new ItemPecaInput(PecaId, 3, 50m)]);
        os.EnviarOrcamento(DateTime.UtcNow);
        os.ClearDomainEvents();
        return os;
    }

    private static Domain.OrdemServico.OrdemServico CriarOsComOrcamentoAprovado()
    {
        var os = CriarOsAguardandoAprovacao();
        os.AprovarOrcamento();
        os.ClearDomainEvents();
        return os;
    }

    // ═══════════════════════════════════════════════
    // Aprovação publica OrcamentoAprovado
    // ═══════════════════════════════════════════════

    [Fact(DisplayName = "AprovarOrcamento: registra OrcamentoAprovado com cliente e valor total, sem mudar o status da OS")]
    public void AprovarOrcamento_RegistraEventoOrcamentoAprovado()
    {
        var os = CriarOsAguardandoAprovacao();

        var result = os.AprovarOrcamento();

        Assert.True(result.IsSuccess);
        var evento = Assert.Single(os.DomainEvents.OfType<OrcamentoAprovado>());
        Assert.Equal(os.Id, evento.OrdemServicoId);
        Assert.Equal(os.Orcamentos[0].Id, evento.OrcamentoId);
        Assert.Equal(ClienteId, evento.ClienteId);
        Assert.Equal(350m, evento.ValorTotal); // 2 × 100 + 3 × 50
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }

    [Fact(DisplayName = "AprovarOrcamento: aprovação inválida não registra OrcamentoAprovado")]
    public void AprovarOrcamento_ComFalha_NaoRegistraEvento()
    {
        var os = CriarOsComOrcamentoAprovado();

        var result = os.AprovarOrcamento(); // já aprovado: não há orçamento Enviado

        Assert.True(result.IsFailure);
        Assert.Empty(os.DomainEvents);
    }

    // ═══════════════════════════════════════════════
    // Pagamento recusado: Aprovado → Rejeitado + OrcamentoRejeitado
    // ═══════════════════════════════════════════════

    [Fact(DisplayName = "PagamentoRecusado: orçamento Aprovado → Rejeitado e registra OrcamentoRejeitado com as peças")]
    public void RejeitarOrcamentoAprovadoPorPagamentoRecusado_TransitaERegistraEvento()
    {
        var os = CriarOsComOrcamentoAprovado();
        var orcamento = os.Orcamentos[0];

        var result = os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(orcamento.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusOrcamento.Rejeitado, orcamento.Status);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
        var evento = Assert.Single(os.DomainEvents.OfType<OrcamentoRejeitado>());
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        var peca = Assert.Single(evento.Pecas);
        Assert.Equal(PecaId, peca.PecaInsumoId);
        Assert.Equal(3, peca.Quantidade);
    }

    [Fact(DisplayName = "PagamentoRecusado: OS apta a novo diagnóstico depois da compensação")]
    public void RejeitarOrcamentoAprovadoPorPagamentoRecusado_PermiteNovoDiagnostico()
    {
        var os = CriarOsComOrcamentoAprovado();
        os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(os.Orcamentos[0].Id);
        os.ClearDomainEvents();

        var result = os.RegistrarDiagnostico(
            "novo diagnóstico",
            [new ItemServicoInput(ServicoId, 1, 80m)],
            [new ItemPecaInput(PecaId, 1, 40m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, os.Orcamentos.Count);
        Assert.Equal(StatusOrcamento.Pendente, os.Orcamentos[1].Status);
        Assert.Single(os.DomainEvents.OfType<OrcamentoGerado>());
    }

    [Theory(DisplayName = "PagamentoRecusado: orçamento que não está Aprovado → TransicaoInvalida e nada muda")]
    [InlineData(StatusOrcamento.Enviado)]
    [InlineData(StatusOrcamento.Rejeitado)]
    public void RejeitarOrcamentoAprovadoPorPagamentoRecusado_OrcamentoNaoAprovado_RetornaErro(StatusOrcamento status)
    {
        var os = CriarOsAguardandoAprovacao();
        if (status == StatusOrcamento.Rejeitado)
        {
            os.RejeitarOrcamento();
            os.ClearDomainEvents();
        }
        var orcamento = os.Orcamentos[0];

        var result = os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(orcamento.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Orcamento.TransicaoInvalida", result.Error.Code);
        Assert.Equal(status, orcamento.Status);
        Assert.Empty(os.DomainEvents);
    }

    [Fact(DisplayName = "PagamentoRecusado: orçamento inexistente → OrcamentoNaoEncontrado")]
    public void RejeitarOrcamentoAprovadoPorPagamentoRecusado_OrcamentoInexistente_RetornaErro()
    {
        var os = CriarOsComOrcamentoAprovado();

        var result = os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(OrcamentoId.Novo());

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.OrcamentoNaoEncontrado", result.Error.Code);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }

    [Fact(DisplayName = "PagamentoRecusado: OS fora de Aguardando Aprovação → TransicaoInvalida")]
    public void RejeitarOrcamentoAprovadoPorPagamentoRecusado_OsEmExecucao_RetornaErro()
    {
        var os = CriarOsComOrcamentoAprovado();
        os.Executar();

        var result = os.RejeitarOrcamentoAprovadoPorPagamentoRecusado(os.Orcamentos[0].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.TransicaoInvalida", result.Error.Code);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }

    [Fact(DisplayName = "Rejeitar (cliente): continua aceitando apenas orçamento Enviado, mesmo existindo a compensação")]
    public void RejeitarOrcamento_OrcamentoAprovado_ContinuaRecusando()
    {
        var os = CriarOsComOrcamentoAprovado();

        var result = os.RejeitarOrcamento();

        Assert.True(result.IsFailure);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }

    // ═══════════════════════════════════════════════
    // Falha na baixa de estoque: → Rejeitado, sem OrcamentoRejeitado
    // ═══════════════════════════════════════════════

    [Theory(DisplayName = "FalhaDeEstoque: orçamento Enviado ou Aprovado → Rejeitado sem registrar OrcamentoRejeitado")]
    [InlineData(StatusOrcamento.Enviado)]
    [InlineData(StatusOrcamento.Aprovado)]
    public void RejeitarOrcamentoPorFalhaNaBaixaDeEstoque_TransitaSemEvento(StatusOrcamento statusInicial)
    {
        var os = statusInicial == StatusOrcamento.Aprovado
            ? CriarOsComOrcamentoAprovado()
            : CriarOsAguardandoAprovacao();
        var orcamento = os.Orcamentos[0];

        var result = os.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(orcamento.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusOrcamento.Rejeitado, orcamento.Status);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
        Assert.Empty(os.DomainEvents);
    }

    [Fact(DisplayName = "FalhaDeEstoque: orçamento Pendente → Rejeitado")]
    public void RejeitarOrcamentoPorFalhaNaBaixaDeEstoque_OrcamentoPendente_Transita()
    {
        var os = Domain.OrdemServico.OrdemServico.AbrirComServicos(
            ClienteId,
            VeiculoId,
            [new ItemServicoInput(ServicoId, 1, 100m)],
            [new ItemPecaInput(PecaId, 1, 50m)]).Value;
        os.ClearDomainEvents();
        var orcamento = os.Orcamentos[0];
        Assert.Equal(StatusOrcamento.Pendente, orcamento.Status);

        var result = os.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(orcamento.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusOrcamento.Rejeitado, orcamento.Status);
        Assert.Empty(os.DomainEvents);
    }

    [Fact(DisplayName = "FalhaDeEstoque: orçamento já Rejeitado → TransicaoInvalida")]
    public void RejeitarOrcamentoPorFalhaNaBaixaDeEstoque_JaRejeitado_RetornaErro()
    {
        var os = CriarOsAguardandoAprovacao();
        os.RejeitarOrcamento();
        os.ClearDomainEvents();

        var result = os.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(os.Orcamentos[0].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Orcamento.TransicaoInvalida", result.Error.Code);
        Assert.Empty(os.DomainEvents);
    }

    [Fact(DisplayName = "FalhaDeEstoque: orçamento inexistente → OrcamentoNaoEncontrado")]
    public void RejeitarOrcamentoPorFalhaNaBaixaDeEstoque_OrcamentoInexistente_RetornaErro()
    {
        var os = CriarOsAguardandoAprovacao();

        var result = os.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(OrcamentoId.Novo());

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.OrcamentoNaoEncontrado", result.Error.Code);
        Assert.Equal(StatusOrcamento.Enviado, os.Orcamentos[0].Status);
    }

    [Fact(DisplayName = "FalhaDeEstoque: OS fora de Aguardando Aprovação → TransicaoInvalida")]
    public void RejeitarOrcamentoPorFalhaNaBaixaDeEstoque_OsEmExecucao_RetornaErro()
    {
        var os = CriarOsComOrcamentoAprovado();
        os.Executar();

        var result = os.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(os.Orcamentos[0].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("OrdemServico.TransicaoInvalida", result.Error.Code);
        Assert.Equal(StatusOrcamento.Aprovado, os.Orcamentos[0].Status);
    }
}
