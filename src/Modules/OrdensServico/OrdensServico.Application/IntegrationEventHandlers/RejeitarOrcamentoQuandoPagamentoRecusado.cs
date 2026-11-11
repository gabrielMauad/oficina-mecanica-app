using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorPagamentoRecusado;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application;
using SharedKernel.Domain;

namespace OrdensServico.Application.IntegrationEventHandlers;

/// <summary>
/// Pagamento recusado: compensação da Saga. O orçamento Aprovado passa a Rejeitado e o evento de
/// domínio <c>OrcamentoRejeitado</c> faz o PecasInsumos estornar as peças; a OS segue em Aguardando
/// Aprovação, apta a um novo diagnóstico.
/// </summary>
public sealed class RejeitarOrcamentoQuandoPagamentoRecusado
    : IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<RejeitarOrcamentoQuandoPagamentoRecusado> _logger;

    public RejeitarOrcamentoQuandoPagamentoRecusado(
        ISender sender,
        ILogger<RejeitarOrcamentoQuandoPagamentoRecusado> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(PagamentoRecusadoIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Result<OrdemServico> result = await _sender.Send(
            new RejeitarOrcamentoPorPagamentoRecusadoCommand(
                integrationEvent.OrdemServicoId, integrationEvent.OrcamentoId),
            cancellationToken);

        if (result.IsFailure)
            _logger.LogError(
                "Não foi possível rejeitar o orçamento {OrcamentoId} da OS {OrdemServicoId} após o pagamento recusado {PagamentoId} ({Motivo}): {Erro}.",
                integrationEvent.OrcamentoId,
                integrationEvent.OrdemServicoId,
                integrationEvent.PagamentoId,
                integrationEvent.Motivo,
                result.Error);
    }
}
