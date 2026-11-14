using System.Net;
using System.Text.Json;
using Api.Middlewares;
using IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IntegrationTests.Api;

/// <summary>
/// Comportamento do host: documento OpenAPI com esquema Bearer e tratamento de exceções em
/// ProblemDetails.
/// </summary>
[Collection("Integration")]
public class ApiHostTests
{
    private readonly OficinaMecanicaWebApplicationFactory _factory;

    public ApiHostTests(OficinaMecanicaWebApplicationFactory factory) => _factory = factory;

    private sealed class ProblemDetailsServiceFake : IProblemDetailsService
    {
        public ProblemDetailsContext? Recebido { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Recebido = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Recebido = context;
            return ValueTask.FromResult(true);
        }
    }

    private WebApplicationFactory<Program> ComOpenApi(bool habilitado) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["OpenApi:Enabled"] = habilitado.ToString() })));

    [Fact(DisplayName = "OpenAPI habilitado → /openapi/v1.json traz o esquema de segurança Bearer (JWT)")]
    public async Task OpenApiHabilitado_DocumentoTemEsquemaBearer()
    {
        using var factory = ComOpenApi(true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var bearer = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Fact(DisplayName = "OpenAPI habilitado → GET / redireciona para /scalar")]
    public async Task OpenApiHabilitado_RaizRedirecionaParaScalar()
    {
        using var factory = ComOpenApi(true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/scalar", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "OpenAPI desabilitado → /openapi/v1.json não é exposto")]
    public async Task OpenApiDesabilitado_DocumentoNaoExposto()
    {
        using var factory = ComOpenApi(false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory(DisplayName = "CustomExceptionHandler: mapeia a exceção para o status HTTP e descreve o erro")]
    [InlineData(typeof(ArgumentException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError)]
    public async Task CustomExceptionHandler_MapeiaStatus(Type tipo, int statusEsperado)
    {
        var servico = new ProblemDetailsServiceFake();
        var handler = new CustomExceptionHandler(servico);
        var contexto = new DefaultHttpContext();
        var excecao = (Exception)Activator.CreateInstance(tipo, "falha simulada")!;

        var tratada = await handler.TryHandleAsync(contexto, excecao, CancellationToken.None);

        Assert.True(tratada);
        Assert.Equal(statusEsperado, contexto.Response.StatusCode);
        Assert.NotNull(servico.Recebido);
        Assert.Equal(tipo.Name, servico.Recebido!.ProblemDetails.Type);
        Assert.Contains("falha simulada", servico.Recebido.ProblemDetails.Detail);
        Assert.Same(excecao, servico.Recebido.Exception);
    }
}
