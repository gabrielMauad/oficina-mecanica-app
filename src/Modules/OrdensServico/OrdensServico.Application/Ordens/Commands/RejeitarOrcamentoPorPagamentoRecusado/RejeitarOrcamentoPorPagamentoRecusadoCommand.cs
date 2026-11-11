using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application;

namespace OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;

/// <summary>
/// Compensação da Saga quando o pagamento é recusado: o orçamento Aprovado volta a Rejeitado e o
/// estoque é estornado pelo evento <c>OrcamentoRejeitado</c>.
/// </summary>
public sealed record RejeitarOrcamentoPorPagamentoRecusadoCommand(Guid OrdemServicoId, Guid OrcamentoId)
    : ICommand<OrdemServico>;
