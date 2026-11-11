using MediatR;
using Microsoft.Extensions.Logging;
using OrdensServico.Application.Gateways;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;

public sealed class RejeitarOrcamentoPorFalhaDeEstoqueHandler
    : IRequestHandler<RejeitarOrcamentoPorFalhaDeEstoqueCommand, Result<OrdemServico>>
{
    private readonly IOrdemServicoGateway _ordemServicoGateway;
    private readonly ILogger<RejeitarOrcamentoPorFalhaDeEstoqueHandler> _logger;

    public RejeitarOrcamentoPorFalhaDeEstoqueHandler(
        IOrdemServicoGateway ordemServicoGateway,
        ILogger<RejeitarOrcamentoPorFalhaDeEstoqueHandler> logger)
    {
        _ordemServicoGateway = ordemServicoGateway;
        _logger = logger;
    }

    public async Task<Result<OrdemServico>> Handle(
        RejeitarOrcamentoPorFalhaDeEstoqueCommand command, CancellationToken ct)
    {
        OrdemServicoId ordemServicoId = new(command.OrdemServicoId);
        OrdemServico? ordemServico = await _ordemServicoGateway.ObterPorId(ordemServicoId, ct);

        if (ordemServico is null)
            return OrdemServicoErrors.NaoEncontrada;

        OrcamentoId orcamentoId = new(command.OrcamentoId);

        // Reentrega do evento (ou orçamento já rejeitado pelo cliente): nada a fazer.
        if (ordemServico.Orcamentos.Any(x => x.Id == orcamentoId && x.Status == StatusOrcamento.Rejeitado))
        {
            _logger.LogInformation(
                "Orçamento {OrcamentoId} da OS {OrdemServicoId} já está Rejeitado; falha de estoque ignorada.",
                command.OrcamentoId,
                command.OrdemServicoId);
            return ordemServico;
        }

        Result<OrdemServico> result = ordemServico.RejeitarOrcamentoPorFalhaNaBaixaDeEstoque(orcamentoId);
        if (result.IsFailure)
            return result.Error;

        OrdemServico os = result.Value;
        await _ordemServicoGateway.Atualizar(os, ct);

        return os;
    }
}
