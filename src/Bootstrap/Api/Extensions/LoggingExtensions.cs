using Api.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Logs;

namespace Api.Extensions;

public static class LoggingExtensions
{
    /// <summary>
    /// Substitui o formatter de console padrão por um formatter JSON estruturado que
    /// carrega trace_id/span_id (Activity.Current) em cada linha, conforme ADR-004. Continua
    /// valendo mesmo com <see cref="AddOtlpLogging"/> habilitado: o stdout segue útil para
    /// <c>kubectl logs</c> e não depende de nenhum backend estar de pé.
    /// </summary>
    public static WebApplicationBuilder AddStructuredJsonLogging(this WebApplicationBuilder builder)
    {
        builder.Logging
            .AddConsole(options => options.FormatterName = TraceJsonConsoleFormatter.FormatterName)
            .AddConsoleFormatter<TraceJsonConsoleFormatter, ConsoleFormatterOptions>();

        return builder;
    }

    /// <summary>
    /// Exporta logs por OTLP (RFC-004), via provedor de logging nativo do OpenTelemetry — a
    /// correlação log↔trace automática na interface do New Relic depende do trace_id/span_id
    /// virem no próprio registro de log OTLP, algo que o <see cref="TraceJsonConsoleFormatter"/>
    /// não pode oferecer (ele só grava no stdout do container, que o New Relic não lê). O
    /// provedor OTel preenche trace_id/span_id nativamente a partir de <c>Activity.Current</c>,
    /// sem código extra aqui. Mensagem formatada, escopos e valores de estado viram atributos do
    /// registro de log.
    /// </summary>
    public static WebApplicationBuilder AddOtlpLogging(this WebApplicationBuilder builder)
    {
        // Mesmo motivo do AddOficinaMecanicaObservability: sem coletor no ambiente "Testing",
        // então o provedor OTel de logging fica fora do host nesse ambiente.
        if (builder.Environment.IsEnvironment("Testing"))
            return builder;

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(ObservabilityExtensions.BuildResourceBuilder(builder));
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.AddOtlpExporter();
        });

        return builder;
    }
}
