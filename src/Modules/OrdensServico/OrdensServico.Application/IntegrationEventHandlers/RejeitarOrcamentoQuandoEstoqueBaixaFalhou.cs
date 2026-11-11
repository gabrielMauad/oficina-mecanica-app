using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Ordens.Commands.RejeitarOrcamentoPorFalhaDeEstoque;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application;
using SharedKernel.Domain;

namespace OrdensServico.Application.IntegrationEventHandlers;

/// <summary>
/// Falha na baixa de estoque: o orçamento passa a Rejeitado. Nada foi baixado, então
/// <c>OrcamentoRejeitado</c> não é publicado (não há o que estornar).
/// </summary>
public sealed class RejeitarOrcamentoQuandoEstoqueBaixaFalhou
    : IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou> _logger;

    public RejeitarOrcamentoQuandoEstoqueBaixaFalhou(
        ISender sender,
        ILogger<RejeitarOrcamentoQuandoEstoqueBaixaFalhou> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(EstoqueBaixaFalhouIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Result<OrdemServico> result = await _sender.Send(
            new RejeitarOrcamentoPorFalhaDeEstoqueCommand(
                integrationEvent.OrdemServicoId, integrationEvent.OrcamentoId),
            cancellationToken);

        if (result.IsFailure)
            _logger.LogError(
                "Não foi possível rejeitar o orçamento {OrcamentoId} da OS {OrdemServicoId} após a falha na baixa de estoque ({Motivo}): {Erro}.",
                integrationEvent.OrcamentoId,
                integrationEvent.OrdemServicoId,
                integrationEvent.Motivo,
                result.Error);
    }
}
