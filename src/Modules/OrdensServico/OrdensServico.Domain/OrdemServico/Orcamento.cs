using SharedKernel.Domain;

namespace OrdensServico.Domain.OrdemServico;

public sealed class Orcamento : Entity<OrcamentoId>
{
    public decimal ValorTotal { get; private set; }
    public StatusOrcamento Status { get; private set; }
    public DateTime DataGeracao { get; private set; }
    public DateTime? DataEnvio { get; private set; }
    public DateTime? DataAprovacao { get; private set; }

    private Orcamento(OrcamentoId id, decimal valorTotal) : base(id)
    {
        ValorTotal = valorTotal;
        Status = StatusOrcamento.Pendente;
        DataGeracao = DateTime.UtcNow;
    }

    private Orcamento(OrcamentoId id, decimal valorTotal, StatusOrcamento status, DateTime dataGeracao,
        DateTime? dataEnvio, DateTime? dataAprovacao) : base(id)
    {
        ValorTotal = valorTotal;
        Status = status;
        DataGeracao = dataGeracao;
        DataEnvio = dataEnvio;
        DataAprovacao = dataAprovacao;
    }

    internal static Orcamento Criar(decimal valorTotal)
        => new(OrcamentoId.Novo(), valorTotal);

    public static Orcamento Reconstituir(OrcamentoId id, decimal valorTotal, StatusOrcamento status,
        DateTime dataGeracao, DateTime? dataEnvio, DateTime? dataAprovacao) =>
        new(id, valorTotal, status, dataGeracao, dataEnvio, dataAprovacao);

    internal Result<Orcamento> Enviar(DateTime dataEnvio)
    {
        if (Status != StatusOrcamento.Pendente)
            return Error.Validation("Orcamento.TransicaoInvalida", "Orçamento só pode ser enviado quando está Pendente.");

        Status = StatusOrcamento.Enviado;
        DataEnvio = dataEnvio;
        return this;
    }

    internal Result<Orcamento> Aprovar()
    {
        if (Status != StatusOrcamento.Enviado)
            return Error.Validation("Orcamento.TransicaoInvalida", "Orçamento só pode ser aprovado quando está Enviado.");

        Status = StatusOrcamento.Aprovado;
        DataAprovacao = DateTime.UtcNow;
        return this;
    }

    internal Result<Orcamento> Rejeitar()
    {
        if (Status != StatusOrcamento.Enviado)
            return Error.Validation("Orcamento.TransicaoInvalida", "Orçamento só pode ser rejeitado quando está Enviado.");

        Status = StatusOrcamento.Rejeitado;
        return this;
    }

    /// <summary>
    /// Compensação do pagamento recusado: desfaz a aprovação (Aprovado → Rejeitado). É uma transição
    /// própria; <see cref="Rejeitar"/> continua aceitando apenas orçamento Enviado.
    /// </summary>
    internal Result<Orcamento> RejeitarAposAprovacao()
    {
        if (Status != StatusOrcamento.Aprovado)
            return Error.Validation("Orcamento.TransicaoInvalida", "Orçamento só pode ter a aprovação desfeita quando está Aprovado.");

        Status = StatusOrcamento.Rejeitado;
        return this;
    }

    /// <summary>
    /// Falha na baixa de estoque: o orçamento ainda não concluiu o fluxo (Pendente, Enviado ou
    /// Aprovado) e passa a Rejeitado.
    /// </summary>
    internal Result<Orcamento> RejeitarPorFalhaDeEstoque()
    {
        if (Status == StatusOrcamento.Rejeitado)
            return Error.Validation("Orcamento.TransicaoInvalida", "Orçamento já está Rejeitado.");

        Status = StatusOrcamento.Rejeitado;
        return this;
    }
}
