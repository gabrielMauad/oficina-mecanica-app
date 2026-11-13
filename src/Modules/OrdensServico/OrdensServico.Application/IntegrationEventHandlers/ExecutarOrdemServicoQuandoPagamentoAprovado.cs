using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Ordens.Commands.ExecutarOrdemServico;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace OrdensServico.Application.IntegrationEventHandlers;

/// <summary>
/// Pagamento aprovado: a OS sai de Aguardando Aprovação e entra em Execução, desde que o orçamento
/// pago (<c>OrcamentoId</c> do evento) exista na OS e esteja Aprovado. É a única forma de iniciar a
/// execução.
/// </summary>
public sealed class ExecutarOrdemServicoQuandoPagamentoAprovado
    : IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IOrdemServicoGateway _ordemServicoGateway;
    private readonly IntegracoesMetrics _metrics;
    private readonly ILogger<ExecutarOrdemServicoQuandoPagamentoAprovado> _logger;

    public ExecutarOrdemServicoQuandoPagamentoAprovado(
        ISender sender,
        IOrdemServicoGateway ordemServicoGateway,
        IntegracoesMetrics metrics,
        ILogger<ExecutarOrdemServicoQuandoPagamentoAprovado> logger)
    {
        _sender = sender;
        _ordemServicoGateway = ordemServicoGateway;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(PagamentoAprovadoIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        OrdemServico? ordemServico = await _ordemServicoGateway.ObterPorId(
            new OrdemServicoId(integrationEvent.OrdemServicoId), cancellationToken);

        if (ordemServico is null)
        {
            _logger.LogError(
                "OS {OrdemServicoId} não encontrada ao processar o pagamento aprovado {PagamentoId}.",
                integrationEvent.OrdemServicoId,
                integrationEvent.PagamentoId);
            RegistrarFalha();
            return;
        }

        // Reentrega do evento: a execução já foi iniciada.
        if (ordemServico.Status is StatusOrdemServico.EmExecucao
            or StatusOrdemServico.Finalizada
            or StatusOrdemServico.Entregue)
        {
            _logger.LogInformation(
                "OS {OrdemServicoId} já está {Status}; pagamento aprovado {PagamentoId} ignorado.",
                integrationEvent.OrdemServicoId,
                ordemServico.Status,
                integrationEvent.PagamentoId);
            return;
        }

        // O pagamento só autoriza a execução se for do orçamento aprovado: um orçamento já Rejeitado
        // (ex.: falha de estoque que chegou depois da aprovação) não pode iniciar a execução, mesmo
        // que outro orçamento da OS esteja Aprovado.
        Orcamento? orcamento = ordemServico.Orcamentos
            .FirstOrDefault(x => x.Id.Value == integrationEvent.OrcamentoId);

        if (orcamento is null || orcamento.Status != StatusOrcamento.Aprovado)
        {
            _logger.LogError(
                "Pagamento aprovado {PagamentoId} para o orçamento {OrcamentoId} da OS {OrdemServicoId}, que {Situacao}: possível cobrança sem execução; a OS não foi executada.",
                integrationEvent.PagamentoId,
                integrationEvent.OrcamentoId,
                integrationEvent.OrdemServicoId,
                orcamento is null ? "não existe na OS" : $"está {orcamento.Status} (não Aprovado)");
            RegistrarFalha();
            return;
        }

        Result<OrdemServico> result = await _sender.Send(
            new ExecutarOrdemServicoCommand(integrationEvent.OrdemServicoId), cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError(
                "Não foi possível executar a OS {OrdemServicoId} após o pagamento aprovado {PagamentoId}: {Erro}.",
                integrationEvent.OrdemServicoId,
                integrationEvent.PagamentoId,
                result.Error);
            RegistrarFalha();
        }
    }

    private void RegistrarFalha() =>
        _metrics.RegistrarFalha(
            nameof(PagamentoAprovadoIntegrationEvent),
            nameof(ExecutarOrdemServicoQuandoPagamentoAprovado));
}
