using MediatR;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.Gateways.Dtos;
using OrdensServico.Application.Metrics;
using OrdensServico.Domain.OrdemServico;
using SharedKernel.Domain;

namespace OrdensServico.Application.Ordens.Commands.RegistrarDiagnostico;

public sealed class RegistrarDiagnosticoHandler : IRequestHandler<RegistrarDiagnosticoCommand, Result<OrdemServico>>
{
    private readonly IServicoGateway _servicoGateway;
    private readonly IPecaDisponibilidadeGateway _pecaDisponibilidadeGateway;
    private readonly IOrdemServicoGateway _gateway;
    private readonly OrdensServicoMetrics _metrics;

    public RegistrarDiagnosticoHandler(
        IServicoGateway servicoGateway,
        IPecaDisponibilidadeGateway pecaDisponibilidadeGateway,
        IOrdemServicoGateway gateway,
        OrdensServicoMetrics metrics
    )
    {
        _servicoGateway = servicoGateway;
        _pecaDisponibilidadeGateway = pecaDisponibilidadeGateway;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task<Result<OrdemServico>> Handle(RegistrarDiagnosticoCommand command, CancellationToken ct)
    {
        OrdemServicoId ordemServicoId = new(command.OrdemServicoId);
        OrdemServico? ordemServico = await _gateway.ObterPorId(ordemServicoId, ct);

        if (ordemServico == null)
            return OrdemServicoErrors.NaoEncontrada;

        Task<Result<List<ItemServicoInput>>> servicosTask = ObterServicosAsync(command.Servicos, ct);
        Task<Result<List<ItemPecaInput>>> pecasTask = ObterPecasAsync(command.Pecas, ct);

        await Task.WhenAll(servicosTask, pecasTask);

        Result<List<ItemServicoInput>> servicosResult = await servicosTask;
        if (servicosResult.IsFailure)
            return servicosResult.Error;

        Result<List<ItemPecaInput>> pecasResult = await pecasTask;
        if (pecasResult.IsFailure)
            return pecasResult.Error;

        List<ItemServicoInput> itemServicoList = servicosResult.Value;
        List<ItemPecaInput> itemPecaList = pecasResult.Value;

        DateTime inicioEtapa = ordemServico.StatusAlteradoEm;
        Result<OrdemServico> resultado = ordemServico.RegistrarDiagnostico(command.DescricaoDiagnostico, itemServicoList, itemPecaList);
        if (resultado.IsFailure)
            return resultado.Error;

        OrdemServico os = resultado.Value;
        _metrics.RegistrarDuracaoEtapa("diagnostico", (DateTime.UtcNow - inicioEtapa).TotalSeconds);

        await _gateway.Atualizar(os, ct);

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
