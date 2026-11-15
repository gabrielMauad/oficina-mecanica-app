using OrdensServico.Adapters.DataSources.Records;
using OrdensServico.Domain.OrdemServico;

namespace OrdensServico.Adapters.DataSources.Mappers;

internal static class OrdemServicoMapper
{
    public static OrdemServicoRecord ToRecord(OrdemServico os) => new()
    {
        Id = os.Id.Value,
        ClienteId = os.ClienteId,
        VeiculoId = os.VeiculoId,
        Status = os.Status.ToString(),
        DescricaoDiagnostico = os.DescricaoDiagnostico,
        NotificadoEm = os.NotificadoEm,
        EntregueEm = os.EntregueEm,
        CriadoEm = os.CriadoEm,
        AtualizadoEm = os.AtualizadoEm,
        StatusAlteradoEm = os.StatusAlteradoEm,
        Versao = os.Versao,
        ItensServico = [.. os.ItensServico.Select(i => new ItemServicoRecord
        {
            Id = i.Id.Value,
            ServicoId = i.ServicoId,
            Quantidade = i.Quantidade,
            PrecoUnitarioSnapshot = i.PrecoUnitarioSnapshot
        })],
        ItensPeca = [.. os.ItensPeca.Select(i => new ItemPecaRecord
        {
            Id = i.Id.Value,
            PecaInsumoId = i.PecaInsumoId,
            Quantidade = i.Quantidade,
            PrecoUnitarioSnapshot = i.PrecoUnitarioSnapshot
        })],
        Orcamentos = [.. os.Orcamentos.Select(oc => new OrcamentoRecord
        {
            Id = oc.Id.Value,
            ValorTotal = oc.ValorTotal,
            Status = oc.Status.ToString(),
            DataGeracao = oc.DataGeracao,
            DataEnvio = oc.DataEnvio,
            DataAprovacao = oc.DataAprovacao
        })]
    };

    public static OrdemServico ToDomain(OrdemServicoRecord r)
    {
        var itensServico = r.ItensServico.Select(i =>
            ItemServico.Reconstituir(new ItemServicoId(i.Id), i.ServicoId, i.Quantidade, i.PrecoUnitarioSnapshot));

        var itensPeca = r.ItensPeca.Select(i =>
            ItemPeca.Reconstituir(new ItemPecaId(i.Id), i.PecaInsumoId, i.Quantidade, i.PrecoUnitarioSnapshot));

        var orcamentos = r.Orcamentos.Select(oc =>
            Orcamento.Reconstituir(
                new OrcamentoId(oc.Id),
                oc.ValorTotal,
                Enum.Parse<StatusOrcamento>(oc.Status),
                oc.DataGeracao,
                oc.DataEnvio,
                oc.DataAprovacao));

        var status = Enum.Parse<StatusOrdemServico>(r.Status);

        return OrdemServico.Reconstituir(
            new OrdemServicoId(r.Id),
            r.ClienteId,
            r.VeiculoId,
            status,
            r.DescricaoDiagnostico,
            r.NotificadoEm,
            r.EntregueEm,
            r.CriadoEm,
            r.AtualizadoEm,
            r.StatusAlteradoEm,
            itensServico,
            itensPeca,
            orcamentos,
            r.Versao);
    }
}
