using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using OrdensServico.Adapters.Controllers;
using OrdensServico.Adapters.DataSources;
using OrdensServico.Adapters.Gateways;
using OrdensServico.Application.Gateways;
using OrdensServico.Application.IntegrationEventHandlers;
using OrdensServico.Application.Metrics;
using OrdensServico.Application.Ordens.Commands.GerarOrdemServico;
using OrdensServico.Application.Ordens.Queries.ListarOrdensParaAcompanhamento;
using OrdensServico.Application.Ordens.Queries.ListarOrdensPorCliente;
using OrdensServico.Contracts.Queries;
using OrdensServico.Infrastructure.Http;
using OrdensServico.Infrastructure.Messaging;
using OrdensServico.Infrastructure.Persistence;
using OrdensServico.Infrastructure.Persistence.Repositories;
using OrdensServico.Infrastructure.Queries;
using SharedKernel.Application;

namespace OrdensServico.Infrastructure;

public static class OrdemServicoModule
{
    public static IServiceCollection AddOrdensServicoModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<OrdensServicoDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdensServicoDbContext>());

        services.AddSingleton<OrdensServicoMetrics>();

        // Barramento de eventos de integração (MassTransit sobre SNS/SQS).
        services.AddMensageria(configuration);

        // Handlers da Application que os consumers da Saga acionam.
        services.AddScoped<IIntegrationEventHandler<PagamentoAprovadoIntegrationEvent>, ExecutarOrdemServicoQuandoPagamentoAprovado>();
        services.AddScoped<IIntegrationEventHandler<PagamentoRecusadoIntegrationEvent>, RejeitarOrcamentoQuandoPagamentoRecusado>();
        services.AddScoped<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>, RejeitarOrcamentoQuandoEstoqueBaixaFalhou>();

        #region DataSources (Repositórios EF)

        services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();

        #endregion

        #region Gateways (Interface Adapters)

        services.AddScoped<IOrdemServicoGateway, OrdemServicoGateway>();
        services.AddScoped<INotificacaoClienteGateway, NotificacaoClienteGateway>();

        // Cadastro e PecasInsumos são serviços externos: consultados por HTTP (HttpClient tipado).
        services.AddGatewaysHttp();

        #endregion

        #region Controllers CA

        services.AddScoped<OrdemServicoController>();

        #endregion

        #region Queries de leitura

        services.AddScoped<IListarOrdensPorClienteReadModel, ListarOrdensPorClienteReadModelImpl>();
        services.AddScoped<IListarOrdensParaAcompanhamentoReadModel, ListarOrdensParaAcompanhamentoReadModelImpl>();

        #endregion

        #region Queries públicas (contratos expostos aos outros módulos)

        services.AddScoped<IOrdemServicoResumoQuery, OrdemServicoResumoQuery>();
        services.AddScoped<IListarOrdensPorClienteQuery, ListarOrdensPorClienteQueryImpl>();

        #endregion

        var applicationAssembly = typeof(GerarOrdemServicoCommand).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(applicationAssembly);

        return services;
    }
}
