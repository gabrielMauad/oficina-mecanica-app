namespace OrdensServico.Adapters.DataSources.Records;

public sealed class OrdemServicoRecord
{
    public Guid Id { get; set; }
    public Guid ClienteId { get; set; }
    public Guid VeiculoId { get; set; }
    public string Status { get; set; } = "";
    public string? DescricaoDiagnostico { get; set; }
    public DateTime? NotificadoEm { get; set; }
    public DateTime? EntregueEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public DateTime StatusAlteradoEm { get; set; }
    /// <summary>Token de concorrência otimista: incrementado a cada gravação da OS.</summary>
    public int Versao { get; set; }
    public List<ItemServicoRecord> ItensServico { get; set; } = [];
    public List<ItemPecaRecord> ItensPeca { get; set; } = [];
    public List<OrcamentoRecord> Orcamentos { get; set; } = [];
}
