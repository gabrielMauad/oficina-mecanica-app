using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using OrdensServico.Application.Gateways;
using OrdensServico.Infrastructure.Http;

namespace OrdemServico.Infrastructure.Tests.Http;

public class GatewaysHttpExtensionsTests
{
    private sealed class RegistraRequisicoesHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requisicoes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requisicoes.Add(request);

            var json = request.RequestUri!.AbsolutePath.Contains("disponibilidade")
                ? """{"disponivel":true,"precoUnitario":10.5}"""
                : """{"id":"00000000-0000-0000-0000-000000000001","nome":"Ana","email":"a@a.com","ativo":true,"unidadeDeMedida":"Un"}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class HttpContextAccessorFake(HttpContext? contexto) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = contexto;
    }

    private static ServiceProvider CriarProvider(
        Dictionary<string, string?> config, RegistraRequisicoesHandler? handler = null, HttpContext? contexto = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGatewaysHttp();

        if (contexto is not null)
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessorFake(contexto));

        if (handler is not null)
            services.ConfigureAll<HttpClientFactoryOptions>(o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ConfigValida() => new()
    {
        ["Servicos:Cadastro:BaseUrl"] = "http://oficina-cadastro:8080",
        ["Servicos:PecasInsumos:BaseUrl"] = "http://oficina-pecas-insumos:8080/"
    };

    [Fact]
    public async Task GatewaysDeCadastro_UsamABaseUrlDeCadastro()
    {
        var handler = new RegistraRequisicoesHandler();
        using var provider = CriarProvider(ConfigValida(), handler);
        var clienteId = Guid.NewGuid();

        await provider.GetRequiredService<IClienteGateway>().ExisteEAtivo(clienteId, CancellationToken.None);

        Assert.Equal($"http://oficina-cadastro:8080/api/v1/clientes/{clienteId}",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task GatewaysDePecasInsumos_UsamABaseUrlDePecasInsumos_MesmoComBarraFinal()
    {
        var handler = new RegistraRequisicoesHandler();
        using var provider = CriarProvider(ConfigValida(), handler);
        var pecaId = Guid.NewGuid();

        await provider.GetRequiredService<IPecaDisponibilidadeGateway>().Verificar(pecaId, 2, CancellationToken.None);

        Assert.Equal($"http://oficina-pecas-insumos:8080/api/v1/pecas-insumos/{pecaId}/disponibilidade?quantidade=2",
            Assert.Single(handler.Requisicoes).RequestUri!.ToString());
    }

    [Fact]
    public async Task Gateways_RepassamOAuthorizationDaRequisicaoDeEntrada()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = "Bearer token-da-oficina";
        var handler = new RegistraRequisicoesHandler();
        using var provider = CriarProvider(ConfigValida(), handler, contexto);

        await provider.GetRequiredService<IClienteGateway>().ObterInfo(Guid.NewGuid(), CancellationToken.None);
        await provider.GetRequiredService<IPecaDisponibilidadeGateway>().Verificar(Guid.NewGuid(), 1, CancellationToken.None);

        Assert.Equal(2, handler.Requisicoes.Count);
        Assert.All(handler.Requisicoes,
            r => Assert.Equal("Bearer token-da-oficina", r.Headers.Authorization!.ToString()));
    }

    [Theory]
    [InlineData("Servicos:Cadastro:BaseUrl")]
    [InlineData("Servicos:PecasInsumos:BaseUrl")]
    public void Opcoes_SemBaseUrl_FalhamNaValidacao(string chaveAusente)
    {
        var config = ConfigValida();
        config.Remove(chaveAusente);
        using var provider = CriarProvider(config);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ServicosOptions>>().Value);

        Assert.Contains(chaveAusente, ex.Message);
    }
}
