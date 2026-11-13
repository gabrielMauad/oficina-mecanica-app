using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application;

namespace OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;

/// <summary>
/// Reação da Saga à falha na baixa de estoque: o orçamento é rejeitado sem publicar
/// <c>OrcamentoRejeitado</c>, pois nada foi baixado e não há o que estornar.
/// </summary>
public sealed record RejeitarOrcamentoPorFalhaDeEstoqueCommand(Guid OrdemServicoId, Guid OrcamentoId)
    : ICommand<OrdemServico>;
