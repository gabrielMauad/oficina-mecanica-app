using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Metrics;

namespace SharedKernel.Application;

public static class SharedKernelModule
{
    public static IServiceCollection AddSharedKernelServices(this IServiceCollection services)
    {
        services.AddScoped<IPendingIntegrationEvents, PendingIntegrationEvents>();
        services.AddScoped<IDomainEventCollector, DomainEventCollector>();
        services.AddSingleton<IntegracoesMetrics>();

        return services;
    }
}

