using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using OficinaMecanica.Contracts.IntegrationEvents;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace IntegrationTests.Infrastructure;

/// <summary>
/// Factory compartilhada por todos os testes de integração via [Collection("Integration")].
/// Sobe um container PostgreSQL real (Testcontainers) uma única vez por sessão de testes,
/// aplica migrations automaticamente (via Program.cs que já chama MigrateAsync no startup),
/// e fornece helpers de autenticação.
///
/// Cadastro e PecasInsumos são serviços externos: os gateways HTTP da OS são atendidos por
/// <see cref="ServicosExternos"/>, e a mensageria roda em memória com o test harness do
/// MassTransit (<see cref="CriarHarness"/>) para verificar o que a OS publica.
/// </summary>
public sealed class OficinaMecanicaWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:15.1")
        .WithDatabase("ordens_servico")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>Fake de Cadastro e PecasInsumos, compartilhado pelos testes da coleção.</summary>
    public FakeServicosExternos ServicosExternos { get; } = new();

    // IAsyncLifetime.InitializeAsync — xunit.v3 chama antes do primeiro teste da coleção
    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
    }

    // Sobrescreve WebApplicationFactory.DisposeAsync e também satisfaz IAsyncLifetime.DisposeAsync
    // (ambos têm a mesma assinatura ValueTask DisposeAsync())
    public override async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Sobrescreve configurações com valores de teste.
        // ConfigureAppConfiguration adiciona fontes APÓS os defaults (appsettings.json),
        // então estes valores têm prioridade.
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(TestConfiguration.Valores(_db.GetConnectionString())));

        builder.ConfigureTestServices(services =>
        {
            // Os HttpClients dos gateways passam a falar com o fake em vez de sair pela rede. O
            // DelegatingHandler que repassa o Authorization continua na cadeia, na frente do fake.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = ServicosExternos));

            // Mesmo transporte em memória da aplicação, agora com o harness para observar publicações.
            services.AddMassTransitTestHarness();
        });
    }

    /// <summary>
    /// Harness do MassTransit do host de teste: permite verificar os eventos de integração publicados.
    /// </summary>
    public ITestHarness CriarHarness() => Services.GetRequiredService<ITestHarness>();

    /// <summary>
    /// Aguarda (até o limite) que a condição fique verdadeira. Os consumers da Saga rodam de forma
    /// assíncrona no barramento em memória, então o efeito no banco aparece depois da publicação.
    /// </summary>
    public static async Task AguardarAsync(Func<Task<bool>> condicao, string descricao, int timeoutSegundos = 15)
    {
        var limite = DateTime.UtcNow.AddSeconds(timeoutSegundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicao()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Condição não atendida no prazo: {descricao}.");
    }

    /// <summary>Lê o JSON de uma OS (GET /api/v1/ordens-servico/{id}) como documento.</summary>
    public static async Task<JsonDocument> ObterOsJsonAsync(HttpClient client, Guid osId)
    {
        var response = await client.GetAsync($"/api/v1/ordens-servico/{osId}");
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    /// <summary>Aguarda a OS chegar ao status informado (ex.: "EmExecucao").</summary>
    public Task AguardarStatusDaOsAsync(HttpClient client, Guid osId, string statusEsperado) =>
        AguardarAsync(async () =>
        {
            using var os = await ObterOsJsonAsync(client, osId);
            return os.RootElement.GetProperty("status").GetString() == statusEsperado;
        }, $"OS {osId} no status {statusEsperado}");

    /// <summary>Aguarda o orçamento (por posição) da OS chegar ao status informado.</summary>
    public Task AguardarStatusDoOrcamentoAsync(HttpClient client, Guid osId, string statusEsperado, int indice = 0) =>
        AguardarAsync(async () =>
        {
            using var os = await ObterOsJsonAsync(client, osId);
            return os.RootElement.GetProperty("orcamentos")[indice].GetProperty("status").GetString() == statusEsperado;
        }, $"orçamento {indice} da OS {osId} no status {statusEsperado}");

    /// <summary>Id do orçamento publicado em <c>OrcamentoGerado</c> para a OS (o último, se houver mais de um).</summary>
    public async Task<Guid> ObterOrcamentoIdPublicadoAsync(Guid osId)
    {
        var harness = CriarHarness();
        Assert.True(await harness.Published.Any<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId));
        return harness.Published.Select<OrcamentoGeradoIntegrationEvent>(
            x => x.Context.Message.OrdemServicoId == osId).Last().Context.Message.OrcamentoId;
    }

    /// <summary>
    /// Faz o papel do serviço Pagamento: publica <c>PagamentoAprovado</c> para o orçamento da OS e
    /// aguarda a OS entrar em execução. É a única forma de a OS sair de Aguardando Aprovação.
    /// </summary>
    public async Task AprovarPagamentoEAguardarExecucaoAsync(HttpClient client, Guid osId)
    {
        var orcamentoId = await ObterOrcamentoIdPublicadoAsync(osId);
        await CriarHarness().Bus.Publish(new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), osId, orcamentoId));
        await AguardarStatusDaOsAsync(client, osId, "EmExecucao");
    }

    /// <summary>
    /// Cria um HttpClient com Authorization: Bearer {token} pré-configurado.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Token de papel Oficina. O login da oficina é emitido pelo serviço de Autenticação
    /// (issuer "oficina-mecanica-app"); este serviço só valida o JWT, então o token é montado
    /// aqui com o mesmo segredo de teste.
    /// </summary>
    public Task<string> GetAuthTokenAsync() => Task.FromResult(GerarToken(
        TestConfiguration.AppIssuer, subject: "admin@integration.test", role: "Oficina"));

    /// <summary>
    /// Gera um token de papel Cliente assinado com o mesmo segredo de teste, simulando o
    /// token que a Function Serverless (emissor "oficina-mecanica-auth") emitiria — ver
    /// RFC-001 §4.1. A aplicação não emite esse token, então é montado diretamente aqui.
    /// </summary>
    public string GetClienteAuthToken(Guid clienteId) => GerarToken(
        TestConfiguration.ClienteAuthIssuer, subject: clienteId.ToString(), role: "Cliente");

    /// <summary>
    /// Cria um HttpClient autenticado com um token de papel Cliente para o clienteId informado.
    /// </summary>
    public HttpClient CreateClienteAuthenticatedClient(Guid clienteId) =>
        CreateAuthenticatedClient(GetClienteAuthToken(clienteId));

    private static string GerarToken(string issuer, string subject, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim("role", role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConfiguration.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: TestConfiguration.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Define a coleção "Integration" — todos os testes com [Collection("Integration")]
/// compartilham a mesma instância de OficinaMecanicaWebApplicationFactory.
/// xunit.v3 garante que os testes da coleção NÃO rodam em paralelo entre si.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection
    : ICollectionFixture<OficinaMecanicaWebApplicationFactory> { }
