using IntegrationTests.Infrastructure;
using Reqnroll;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OrdensServico.Bdd.Support;

/// <summary>
/// Ciclo de vida do ambiente compartilhado por todas as features: um único PostgreSQL
/// (Testcontainers) e um único host da API, com a mensageria em memória observada pelo harness
/// do MassTransit. Cada cenário usa dados próprios (ids únicos), então não precisa limpar o banco.
/// </summary>
[Binding]
public static class AmbienteBdd
{
    public static OficinaMecanicaWebApplicationFactory Factory { get; private set; } = null!;

    [BeforeTestRun]
    public static async Task IniciarAsync()
    {
        Factory = new OficinaMecanicaWebApplicationFactory();
        await Factory.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task EncerrarAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
    }
}
