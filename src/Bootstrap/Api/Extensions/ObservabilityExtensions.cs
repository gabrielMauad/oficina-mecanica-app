using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Api.Extensions;

/// <summary>
/// Configura o SDK do OpenTelemetry (traces + métricas), exportado via OTLP (ADR-004). O
/// destino é resolvido pelas variáveis de ambiente padrão do OpenTelemetry
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>,
/// <c>OTEL_EXPORTER_OTLP_HEADERS</c>), lidas automaticamente pelo
/// <see cref="OpenTelemetry.Exporter.OtlpExporterOptions"/> — não há nada específico de
/// fornecedor (New Relic/Datadog/etc.) neste código; RFC-004 escolheu o New Relic como destino,
/// mas a troca continua sendo só variável de ambiente.
/// </summary>
public static class ObservabilityExtensions
{
    // Meters de negócio criados por outra tarefa (em paralelo), registrados aqui apenas pelo
    // nome — não exige referência às classes que os instanciam.
    private static readonly string[] ApplicationMeterNames =
    [
        "OficinaMecanica.OrdensServico",
        "OficinaMecanica.Integracoes"
    ];

    public static WebApplicationBuilder AddOficinaMecanicaObservability(this WebApplicationBuilder builder)
    {
        // No ambiente "Testing" (WebApplicationFactory dos testes de integração) não existe
        // coletor OTLP disponível: manter o SDK fora do host evita tentativas de exportação
        // (retries/timeouts) e mantém a suíte de testes rápida e determinística.
        if (builder.Environment.IsEnvironment("Testing"))
            return builder;

        var resourceBuilder = BuildResourceBuilder(builder);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                // ADR-004: amostragem em 100% neste projeto, para que nenhum trace usado como
                // evidência (vídeo de entrega) seja descartado. Deliberado para o volume deste
                // projeto — NÃO seria adequado em um ambiente de produção real de alto tráfego,
                // onde amostragem parcial é necessária para controlar custo/volume.
                .SetSampler(new AlwaysOnSampler())
                .AddAspNetCoreInstrumentation() // requisições de entrada
                .AddHttpClientInstrumentation() // chamadas HTTP de saída
                .AddNpgsql() // comandos SQL executados via Npgsql/EF Core, aninhados no span da requisição
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation() // latência das APIs
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation() // CPU, memória e GC do processo
                .AddMeter(ApplicationMeterNames)
                // RFC-004/New Relic recomenda temporalidade delta para métricas OTLP (o backend é
                // delta-nativo; cumulativo custa mais memória/ingestão e é a causa mais comum de
                // somas erradas em contadores como "volume diário de OS" quando o dashboard soma
                // valores já cumulativos). Setado em código — não em variável de ambiente — porque
                // é o único jeito garantido de valer nesta versão do SDK (1.18.0).
                .AddOtlpExporter((_, readerOptions) =>
                    readerOptions.TemporalityPreference = MetricReaderTemporalityPreference.Delta));

        return builder;
    }

    /// <summary>
    /// Resource comum (nome/namespace do serviço + ambiente) compartilhado entre traces,
    /// métricas (acima) e logs (<see cref="LoggingExtensions.AddOtlpLogging"/>), para que os três
    /// sinais apareçam correlacionados sob a mesma entidade no backend.
    /// </summary>
    internal static ResourceBuilder BuildResourceBuilder(WebApplicationBuilder builder) =>
        ResourceBuilder.CreateDefault()
            .AddService(serviceName: "oficina-mecanica-api", serviceNamespace: "oficina-mecanica")
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
            ]);
}
