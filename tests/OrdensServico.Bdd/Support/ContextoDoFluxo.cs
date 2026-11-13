namespace OrdensServico.Bdd.Support;

/// <summary>
/// Estado de um cenário, injetado nas classes de steps (um por cenário).
/// </summary>
public sealed class ContextoDoFluxo : IDisposable
{
    public Guid ClienteId { get; set; }
    public Guid VeiculoId { get; set; }
    public Guid ServicoId { get; set; }
    public Guid PecaId { get; set; }

    public Guid OsId { get; set; }

    /// <summary>Orçamentos gerados da OS, na ordem de publicação de <c>OrcamentoGerado</c>.</summary>
    public List<Guid> OrcamentoIds { get; } = [];

    public Guid OrcamentoAtualId => OrcamentoIds[^1];
    public int IndiceDoOrcamentoAtual => OrcamentoIds.Count - 1;

    public HttpClient Oficina { get; set; } = null!;

    public void Dispose() => Oficina?.Dispose();
}
