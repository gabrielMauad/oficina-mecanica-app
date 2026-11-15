using System.Collections.Concurrent;
using OrdensServico.Adapters.DataSources;
using OrdensServico.Adapters.DataSources.Records;

namespace IntegrationTests.Infrastructure;

/// <summary>
/// Reproduz de forma determinística uma corrida na gravação da OS: antes da próxima gravação de uma
/// OS escolhida, executa uma ação concorrente (por exemplo, outra requisição ou um consumer que
/// grava a mesma OS), exatamente no intervalo entre a leitura e a escrita da operação em curso.
/// </summary>
public sealed class SimuladorDeConcorrencia
{
    private readonly ConcurrentDictionary<Guid, Func<Task>> _acoes = new();

    /// <summary>Arma uma ação de uso único, disparada antes da próxima gravação da OS informada.</summary>
    public void Armar(Guid ordemServicoId, Func<Task> acaoConcorrente) =>
        _acoes[ordemServicoId] = acaoConcorrente;

    /// <summary>Indica se a ação armada para a OS já foi disparada.</summary>
    public bool FoiDisparada(Guid ordemServicoId) => !_acoes.ContainsKey(ordemServicoId);

    internal async Task AntesDeAtualizarAsync(Guid ordemServicoId)
    {
        if (_acoes.TryRemove(ordemServicoId, out var acao))
            await acao();
    }
}

/// <summary>Decorator do repositório da OS que aciona o <see cref="SimuladorDeConcorrencia"/>.</summary>
internal sealed class OrdemServicoRepositoryComConcorrenciaSimulada(
    IOrdemServicoRepository interno, SimuladorDeConcorrencia simulador) : IOrdemServicoRepository
{
    public Task Adicionar(OrdemServicoRecord ordemServico, CancellationToken ct = default) =>
        interno.Adicionar(ordemServico, ct);

    public Task<OrdemServicoRecord?> ObterPorId(Guid id, CancellationToken ct = default) =>
        interno.ObterPorId(id, ct);

    public async Task Atualizar(OrdemServicoRecord ordemServico, CancellationToken ct = default)
    {
        await simulador.AntesDeAtualizarAsync(ordemServico.Id);
        await interno.Atualizar(ordemServico, ct);
    }
}
