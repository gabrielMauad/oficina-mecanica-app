using FluentValidation;
using OrdensServico.Domain.OrdemServico;

namespace OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;

public sealed class RegistrarDiagnosticoValidator : AbstractValidator<RegistrarDiagnosticoCommand>
{
    public RegistrarDiagnosticoValidator()
    {
        RuleFor(x => x.OrdemServicoId)
            .NotEmpty();

        RuleFor(x => x.DescricaoDiagnostico)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.Servicos)
            .NotEmpty()
            .WithMessage("Ao menos um serviço deve ser informado.");

        RuleForEach(x => x.Servicos).ChildRules(s =>
        {
            s.RuleFor(x => x.ServicoId)
                .NotEmpty()
                .WithMessage("O ID do serviço deve ser informado.");

            s.RuleFor(x => x.Quantidade)
            .InclusiveBetween(1, LimitesDoOrcamento.QuantidadeMaximaPorItem)
            .WithMessage($"A quantidade deve estar entre 1 e {LimitesDoOrcamento.QuantidadeMaximaPorItem}.");
        });

        RuleFor(x => x.Pecas)
            .NotEmpty()
            .WithMessage("Ao menos uma peça deve ser informada.");

        RuleForEach(x => x.Pecas).ChildRules(p =>
        {
            p.RuleFor(x => x.PecaInsumoId)
            .NotEmpty()
            .WithMessage("O ID da peça deve ser informado.");

            p.RuleFor(x => x.Quantidade)
            .InclusiveBetween(1, LimitesDoOrcamento.QuantidadeMaximaPorItem)
            .WithMessage($"A quantidade deve estar entre 1 e {LimitesDoOrcamento.QuantidadeMaximaPorItem}.");
        });
    }
}
