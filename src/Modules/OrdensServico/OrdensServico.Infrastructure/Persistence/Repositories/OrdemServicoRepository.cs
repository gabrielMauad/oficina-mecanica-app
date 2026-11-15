using Microsoft.EntityFrameworkCore;
using OrdensServico.Adapters.DataSources;
using OrdensServico.Adapters.DataSources.Records;

namespace OrdensServico.Infrastructure.Persistence.Repositories;

internal sealed class OrdemServicoRepository : IOrdemServicoRepository
{
    private readonly OrdensServicoDbContext _context;
    public OrdemServicoRepository(OrdensServicoDbContext context) => _context = context;

    public Task Adicionar(OrdemServicoRecord ordemServico, CancellationToken ct = default)
    {
        _context.OrdensServico.Add(ordemServico);
        return Task.CompletedTask;
    }

    public Task<OrdemServicoRecord?> ObterPorId(Guid id, CancellationToken ct = default) =>
        _context.OrdensServico
            .AsNoTracking()
            .Include(os => os.ItensServico)
            .Include(os => os.ItensPeca)
            .Include(os => os.Orcamentos)
            .FirstOrDefaultAsync(os => os.Id == id, ct);

    public async Task Atualizar(OrdemServicoRecord incoming, CancellationToken ct = default)
    {
        var tracked = await _context.OrdensServico
            .Include(o => o.ItensServico)
            .Include(o => o.ItensPeca)
            .Include(o => o.Orcamentos)
            .FirstOrDefaultAsync(o => o.Id == incoming.Id, ct);
        if (tracked is null) return;

        // O token de concorrência é o da leitura feita pelo handler (incoming.Versao), não o da
        // releitura acima: assim o UPDATE falha se outra operação gravou a OS no intervalo. A
        // versão sobe a cada gravação, o que também força o UPDATE da OS quando só os filhos mudam.
        var entry = _context.Entry(tracked);
        entry.CurrentValues.SetValues(incoming);
        entry.Property(o => o.Versao).OriginalValue = incoming.Versao;
        entry.Property(o => o.Versao).CurrentValue = incoming.Versao + 1;

        Sync(_context, tracked.ItensServico, incoming.ItensServico, r => r.Id);
        Sync(_context, tracked.ItensPeca, incoming.ItensPeca, r => r.Id);
        Sync(_context, tracked.Orcamentos, incoming.Orcamentos, r => r.Id);
    }

    private static void Sync<T>(DbContext ctx, List<T> atuais, List<T> novos, Func<T, Guid> key) where T : class
    {
        foreach (var a in atuais.Where(a => novos.All(n => key(n) != key(a))).ToList())
            ctx.Remove(a);

        foreach (var n in novos)
        {
            var existente = atuais.FirstOrDefault(a => key(a) == key(n));
            if (existente is null)
            {
                atuais.Add(n);
                ctx.Entry(n).State = EntityState.Added;
            }
            else ctx.Entry(existente).CurrentValues.SetValues(n);
        }
    }
}
