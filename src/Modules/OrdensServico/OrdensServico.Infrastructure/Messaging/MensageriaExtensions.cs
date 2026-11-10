using Amazon.SimpleNotificationService;
using Amazon.SQS;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application;

namespace OrdensServico.Infrastructure.Messaging;

public static class MensageriaExtensions
{
    public const string PrefixoEndpoints = "ordens-servico";

    private static readonly TimeSpan[] IntervalosDeRetry =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)
    ];

    /// <summary>
    /// Configura o MassTransit (transporte por <c>Mensageria:Transporte</c>) e registra o
    /// <see cref="MassTransitIntegrationEventBus"/> como <see cref="IIntegrationEventBus"/>.
    /// </summary>
    public static IServiceCollection AddMensageria(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MensageriaOptions.SectionName).Get<MensageriaOptions>()
            ?? new MensageriaOptions();

        var formatter = new KebabCaseEndpointNameFormatter(PrefixoEndpoints, false);

        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(formatter);

            switch (options.Transporte)
            {
                case MensageriaOptions.TransporteInMemory:
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.UseMessageRetry(r => r.Intervals(IntervalosDeRetry));
                        cfg.ConfigureEndpoints(context, formatter);
                    });
                    break;

                case MensageriaOptions.TransporteAmazonSqs:
                    x.UsingAmazonSqs((context, cfg) =>
                    {
                        cfg.Host(options.Regiao, host => ConfigurarHostAmazon(host, options));
                        cfg.UseMessageRetry(r => r.Intervals(IntervalosDeRetry));
                        cfg.ConfigureEndpoints(context, formatter);
                    });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Mensageria:Transporte inválido: '{options.Transporte}'. " +
                        $"Valores aceitos: {MensageriaOptions.TransporteAmazonSqs}, {MensageriaOptions.TransporteInMemory}.");
            }
        });

        services.AddScoped<IIntegrationEventBus, MassTransitIntegrationEventBus>();

        return services;
    }

    private static void ConfigurarHostAmazon(IAmazonSqsHostConfigurator host, MensageriaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
        {
            host.AccessKey(options.AccessKey);
            host.SecretKey(options.SecretKey);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            host.Config(new AmazonSQSConfig { ServiceURL = options.ServiceUrl });
            host.Config(new AmazonSimpleNotificationServiceConfig { ServiceURL = options.ServiceUrl });
        }
    }
}
