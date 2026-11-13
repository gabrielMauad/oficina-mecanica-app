using FluentValidation;

namespace OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;

public sealed class RejeitarOrcamentoPorPagamentoRecusadoValidator
    : AbstractValidator<RejeitarOrcamentoPorPagamentoRecusadoCommand>
{
    public RejeitarOrcamentoPorPagamentoRecusadoValidator()
    {
        RuleFor(x => x.OrdemServicoId)
            .NotEmpty()
            .WithMessage("O ID da ordem de serviço não pode ser vazio.");

        RuleFor(x => x.OrcamentoId)
            .NotEmpty()
            .WithMessage("O ID do orçamento não pode ser vazio.");
    }
}
