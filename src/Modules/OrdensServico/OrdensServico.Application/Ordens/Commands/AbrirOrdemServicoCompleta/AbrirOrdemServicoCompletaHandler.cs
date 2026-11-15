using MediatR;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;
using OrdensServico.Application.Metrics;
using OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Ordens.Commands.AbrirOrdemServicoCompleta;

public sealed class AbrirOrdemServicoCompletaHandler : IRequestHandler<AbrirOrdemServicoCompletaCommand, Result<OrdemServico>>
{
    private readonly IClienteGateway _clienteGateway;
    private readonly IVeiculoGateway _veiculoGateway;
    private readonly IServicoGateway _servicoGateway;
    private readonly IPecaDisponibilidadeGateway _pecaDisponibilidadeGateway;
    private readonly IOrdemServicoGateway _ordemServicoGateway;
    private readonly OrdensServicoMetrics _metrics;

    public AbrirOrdemServicoCompletaHandler(
        IClienteGateway clienteGateway,
        IVeiculoGateway veiculoGateway,
        IServicoGateway servicoGateway,
        IPecaDisponibilidadeGateway pecaDisponibilidadeGateway,
        IOrdemServicoGateway ordemServicoGateway,
        OrdensServicoMetrics metrics
    )
    {
        _clienteGateway = clienteGateway;
        _veiculoGateway = veiculoGateway;
        _servicoGateway = servicoGateway;
        _pecaDisponibilidadeGateway = pecaDisponibilidadeGateway;
        _ordemServicoGateway = ordemServicoGateway;
        _metrics = metrics;
    }

    public async Task<Result<OrdemServico>> Handle(AbrirOrdemServicoCompletaCommand command, CancellationToken ct)
    {
        if (!await _clienteGateway.ExisteEAtivo(command.ClienteId, ct))
            return OrdemServicoErrors.ClienteInexistenteOuInativo;

        if (!await _veiculoGateway.ExisteEPertenceAoCliente(command.VeiculoId, command.ClienteId, ct))
            return OrdemServicoErrors.VeiculoInexistenteOuNaoPertenceAoCliente;

        Result<List<ItemServicoInput>> servicosResult = await ObterServicosAsync(command.Servicos, ct);
        if (servicosResult.IsFailure)
            return servicosResult.Error;

        Result<List<ItemPecaInput>> pecasResult = await ObterPecasAsync(command.Pecas, ct);
        if (pecasResult.IsFailure)
            return pecasResult.Error;

        var result = OrdemServico.AbrirComServicos(
            command.ClienteId,
            command.VeiculoId,
            servicosResult.Value,
            pecasResult.Value);
        if (result.IsFailure) return result.Error;

        var os = result.Value;
        await _ordemServicoGateway.Adicionar(os, ct);
        _metrics.RegistrarOrdemAberta("completa");

        return os;
    }

    private async Task<Result<List<ItemServicoInput>>> ObterServicosAsync(
        IReadOnlyList<ServicoItem> servicos,
        CancellationToken ct)
    {
        List<ItemServicoInput> itemServicoList = [];
        foreach (var servico in servicos)
        {
            decimal? snapshotPreco = await _servicoGateway.ObterPreco(servico.ServicoId, ct);
            if (snapshotPreco == null)
                return OrdemServicoErrors.ServicoNaoEncontrado;
            if (!await _servicoGateway.EstaAtivo(servico.ServicoId, ct))
                return OrdemServicoErrors.ServicoDesativado;
            itemServicoList.Add(new ItemServicoInput(servico.ServicoId, servico.Quantidade, snapshotPreco.Value));
        }

        return itemServicoList;
    }

    private async Task<Result<List<ItemPecaInput>>> ObterPecasAsync(
        IReadOnlyList<PecaItem> pecas,
        CancellationToken ct)
    {
        List<ItemPecaInput> itemPecaList = [];
        foreach (var peca in pecas)
        {
            PecaDisponibilidade? disponibilidade = await _pecaDisponibilidadeGateway.Verificar(peca.PecaInsumoId, peca.Quantidade, ct);
            if (disponibilidade == null)
                return OrdemServicoErrors.PecaNaoEncontrada;
            if (!disponibilidade.Disponivel)
                return OrdemServicoErrors.PecaIndisponivel;

            itemPecaList.Add(new ItemPecaInput(peca.PecaInsumoId, peca.Quantidade, disponibilidade.PrecoUnitario));
        }

        return itemPecaList;
    }
}
