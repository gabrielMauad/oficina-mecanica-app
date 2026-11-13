using MediatR;
using Microsoft.Extensions.Logging;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;
using OrdensServico.Domain.OrdemServico;
using OrdensServico.Domain.OrdemServico.Events;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace OrdensServico.Application.DomainEventHandlers;

public sealed class EnviarOrcamentoAoCliente : INotificationHandler<OrcamentoGerado>
{
    private readonly IOrdemServicoGateway _ordemServicoGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificacaoClienteGateway _notificacaoClienteGateway;
    private readonly IClienteGateway _clienteGateway;
    private readonly IVeiculoGateway _veiculoGateway;
    private readonly IServicoGateway _servicoGateway;
    private readonly IPecaInsumoInfoGateway _pecaInsumoInfoGateway;
    private readonly IntegracoesMetrics _metrics;
    private readonly ILogger<EnviarOrcamentoAoCliente> _logger;

    public EnviarOrcamentoAoCliente(
        IOrdemServicoGateway ordemServicoGateway,
        IUnitOfWork unitOfWork,
        INotificacaoClienteGateway notificacaoClienteGateway,
        IClienteGateway clienteGateway,
        IVeiculoGateway veiculoGateway,
        IServicoGateway servicoGateway,
        IPecaInsumoInfoGateway pecaInsumoInfoGateway,
        IntegracoesMetrics metrics,
        ILogger<EnviarOrcamentoAoCliente> logger)
    {
        _ordemServicoGateway = ordemServicoGateway;
        _unitOfWork = unitOfWork;
        _notificacaoClienteGateway = notificacaoClienteGateway;
        _clienteGateway = clienteGateway;
        _veiculoGateway = veiculoGateway;
        _servicoGateway = servicoGateway;
        _pecaInsumoInfoGateway = pecaInsumoInfoGateway;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(OrcamentoGerado notification, CancellationToken ct)
    {
        OrdemServico? ordemServico = await _ordemServicoGateway.ObterPorId(notification.OrdemServicoId, ct);
        if (ordemServico is null)
        {
            _logger.LogError(
                "OS {OrdemServicoId} não encontrada ao tentar enviar orçamento após OrcamentoGerado.",
                notification.OrdemServicoId);
            return;
        }

        Result<OrdemServico> result = ordemServico.EnviarOrcamento(DateTime.UtcNow);
        if (result.IsFailure)
        {
            _logger.LogError(
                "Falha ao chamar EnviarOrcamento na OS {OrdemServicoId}: {Erro}.",
                notification.OrdemServicoId,
                result.Error);
            return;
        }

        await _ordemServicoGateway.Atualizar(ordemServico, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // O e-mail é um efeito secundário: uma falha ao montá-lo ou enviá-lo (ex.: Cadastro fora do ar)
        // não pode impedir os demais handlers do OrcamentoGerado (publicação do evento que dispara a
        // baixa de estoque) nem mudar a resposta da requisição.
        try
        {
            await NotificarCliente(ordemServico, notification, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _metrics.RegistrarFalha(nameof(OrcamentoGerado), nameof(EnviarOrcamentoAoCliente));
            _logger.LogError(
                ex,
                "Falha ao enviar o orçamento por e-mail ao cliente da OS {OrdemServicoId}; o fluxo segue sem o e-mail.",
                notification.OrdemServicoId);
        }
    }

    private async Task NotificarCliente(OrdemServico ordemServico, OrcamentoGerado notification, CancellationToken ct)
    {
        ClienteInfo? cliente = await _clienteGateway.ObterInfo(ordemServico.ClienteId, ct);
        string? placa = await _veiculoGateway.ObterPlaca(ordemServico.VeiculoId, ct);

        if (cliente is null || placa is null)
        {
            _logger.LogWarning(
                "Não foi possível notificar cliente da OS {OrdemServicoId}: cliente={ClienteEncontrado}, placa={PlacaEncontrada}.",
                notification.OrdemServicoId,
                cliente is not null,
                placa is not null);
            return;
        }

        List<ServicoEmailItem> servicos = [];
        foreach (ItemServico item in ordemServico.ItensServico)
        {
            string? nome = await _servicoGateway.ObterNome(item.ServicoId, ct);
            if (nome is not null)
                servicos.Add(new ServicoEmailItem(nome, item.Quantidade, item.PrecoUnitarioSnapshot));
        }

        List<PecaEmailItem> pecas = [];
        foreach (ItemPeca item in ordemServico.ItensPeca)
        {
            PecaInsumoInfo? info = await _pecaInsumoInfoGateway.Obter(item.PecaInsumoId, ct);
            if (info is not null)
                pecas.Add(new PecaEmailItem(info.Nome, item.Quantidade, info.UnidadeMedida, item.PrecoUnitarioSnapshot));
        }

        await _notificacaoClienteGateway.NotificarOrcamentoPronto(
            cliente.Nome,
            cliente.Email,
            placa,
            servicos,
            pecas,
            notification.ValorTotal,
            ct);
    }
}
