using MediatR;
using Microsoft.Extensions.Logging;
using OrdensServico.Application.Gateways;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;

public sealed class RejeitarOrcamentoPorPagamentoRecusadoHandler
    : IRequestHandler<RejeitarOrcamentoPorPagamentoRecusadoCommand, Result<OrdemServico>>
{
    private readonly IOrdemServicoGateway _ordemServicoGateway;
    private readonly ILogger<RejeitarOrcamentoPorPagamentoRecusadoHandler> _logger;

    public RejeitarOrcamentoPorPagamentoRecusadoHandler(
        IOrdemServicoGateway ordemServicoGateway,
        ILogger<RejeitarOrcamentoPorPagamentoRecusadoHandler> logger)
    {
        _ordemServicoGateway = ordemServicoGateway;
        _logger = logger;
    }

    public async Task<Result<OrdemServico>> Handle(
        RejeitarOrcamentoPorPagamentoRecusadoCommand command, CancellationToken ct)
    {
        OrdemServicoId ordemServicoId = new(command.OrdemServicoId);
        OrdemServico? ordemServico = await _ordemServicoGateway.ObterPorId(ordemServicoId, ct);

        if (ordemServico is null)
            return OrdemServicoErrors.NaoEncontrada;

        OrcamentoId orcamentoId = new(command.OrcamentoId);

        // Reentrega do evento: a compensação já foi aplicada, não há o que refazer nem estornar de novo.
        if (ordemServico.Orcamentos.Any(x => x.Id == orcamentoId && x.Status == StatusOrcamento.Rejeitado))
        {
            _logger.LogInformation(
                "Orçamento {OrcamentoId} da OS {OrdemServicoId} já está Rejeitado; pagamento recusado ignorado.",
                command.OrcamentoId,
                command.OrdemServicoId);
            return ordemServico;
        }

        Result<OrdemServico> result = ordemServico.RejeitarOrcamentoAprovadoPorPagamentoRecusado(orcamentoId);
        if (result.IsFailure)
            return result.Error;

        OrdemServico os = result.Value;
        await _ordemServicoGateway.Atualizar(os, ct);

        return os;
    }
}
