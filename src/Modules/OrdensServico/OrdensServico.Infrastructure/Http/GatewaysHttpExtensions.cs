using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrdensServico.Adapters.Gateways;
using OrdensServico.Application.Gateways;

namespace OrdensServico.Infrastructure.Http;

public static class GatewaysHttpExtensions
{
    /// <summary>
    /// Registra os gateways que consultam Cadastro e PecasInsumos por <see cref="HttpClient"/> tipado.
    /// A URL base é lida das opções (<see cref="ServicosOptions"/>) quando o cliente é criado e
    /// validada na partida da aplicação.
    /// </summary>
    public static IServiceCollection AddGatewaysHttp(this IServiceCollection services)
    {
        services.AddOptions<ServicosOptions>()
            .BindConfiguration(ServicosOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Cadastro.BaseUrl),
                "Servicos:Cadastro:BaseUrl não configurado.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.PecasInsumos.BaseUrl),
                "Servicos:PecasInsumos:BaseUrl não configurado.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<EncaminharAuthorizationHandler>();

        services.AddHttpClient<IClienteGateway, ClienteGateway>(ConfigurarCadastro)
            .AddHttpMessageHandler<EncaminharAuthorizationHandler>();
        services.AddHttpClient<IVeiculoGateway, VeiculoGateway>(ConfigurarCadastro)
            .AddHttpMessageHandler<EncaminharAuthorizationHandler>();
        services.AddHttpClient<IServicoGateway, ServicoGateway>(ConfigurarCadastro)
            .AddHttpMessageHandler<EncaminharAuthorizationHandler>();

        services.AddHttpClient<IPecaDisponibilidadeGateway, PecaDisponibilidadeGateway>(ConfigurarPecasInsumos)
            .AddHttpMessageHandler<EncaminharAuthorizationHandler>();
        services.AddHttpClient<IPecaInsumoInfoGateway, PecaInsumoInfoGateway>(ConfigurarPecasInsumos)
            .AddHttpMessageHandler<EncaminharAuthorizationHandler>();

        return services;
    }

    private static void ConfigurarCadastro(IServiceProvider sp, HttpClient client) =>
        client.BaseAddress = MontarBaseAddress(sp.GetRequiredService<IOptions<ServicosOptions>>().Value.Cadastro);

    private static void ConfigurarPecasInsumos(IServiceProvider sp, HttpClient client) =>
        client.BaseAddress = MontarBaseAddress(sp.GetRequiredService<IOptions<ServicosOptions>>().Value.PecasInsumos);

    // A barra final garante que caminhos relativos ("api/v1/...") sejam anexados à URL base.
    private static Uri MontarBaseAddress(ServicoRemotoOptions servico) =>
        new(servico.BaseUrl!.TrimEnd('/') + "/");
}
