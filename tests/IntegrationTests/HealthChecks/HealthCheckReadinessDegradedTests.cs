using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using IntegrationTests.Infrastructure;
using Testcontainers.PostgreSql;

namespace IntegrationTests.HealthChecks;

/// <summary>
/// Sobe seu próprio container PostgreSQL (independente do compartilhado pela coleção
/// "Integration") para poder derrubá-lo depois do startup e comprovar que /healthz/ready
/// fica não-saudável enquanto /healthz/live continua respondendo normalmente.
/// </summary>
public sealed class HealthCheckReadinessDegradedTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithDatabase("ordens_servico")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(TestConfiguration.Valores(_db.GetConnectionString()));
            });
        });

        // Força a criação do host (e a execução das migrations) enquanto o banco está de pé.
        using var warmupClient = _factory.CreateClient();
        var warmup = await warmupClient.GetAsync("/healthz/live");
        warmup.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact(DisplayName = "GET /healthz/ready — retorna 503 quando o PostgreSQL está indisponível; /healthz/live continua 200")]
    public async Task Ready_DeveRetornar503_QuandoPostgreSqlIndisponivel_LiveContinuaSaudavel()
    {
        // Arrange — derruba o banco depois que a aplicação já subiu
        await _db.StopAsync();

        using var client = _factory.CreateClient();

        // Act
        var readyResponse = await client.GetAsync("/healthz/ready");
        var liveResponse = await client.GetAsync("/healthz/live");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
    }
}
