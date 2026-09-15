using Api.Extensions;
using Api.Middlewares;
using Api.OpenApi;
using Autenticacao.Infrastructure;
using Autenticacao.Web;
using Cadastro.Infrastructure;
using Cadastro.Infrastructure.Persistence;
using Cadastro.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OrdensServico.Infrastructure;
using OrdensServico.Infrastructure.Persistence;
using OrdensServico.Web;
using PecasInsumos.Infrastructure;
using PecasInsumos.Infrastructure.Persistence;
using PecasInsumos.Web;
using SharedKernel.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredJsonLogging();
builder.AddOtlpLogging();
builder.AddOficinaMecanicaObservability();

builder.Services.AddOficinaMecanicaAuthentication(builder.Configuration);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var hasAuthorize = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        var hasAllowAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        if (hasAuthorize && !hasAllowAnonymous)
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("Bearer"),
                        new List<string>()
                    }
                }
            ];
        }

        return Task.CompletedTask;
    });
});


builder.Services.AddApiHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddAutenticacaoModule(builder.Configuration);
builder.Services.AddCadastroModule(builder.Configuration);
builder.Services.AddSharedKernelServices();
builder.Services.AddPecasInsumosModule(builder.Configuration);
builder.Services.AddOrdensServicoModule(builder.Configuration);

builder.Services.AddControllers()
    .AddApplicationPart(typeof(AutenticacaoAssemblyMarker).Assembly)
    .AddApplicationPart(typeof(CadastroAssemblyMarker).Assembly)
    .AddApplicationPart(typeof(PecasInsumosAssemblyMarker).Assembly)
    .AddApplicationPart(typeof(OrdensServicoAssemblyMarker).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapOpenApiDocumentation();
app.MapApiHealthChecks();

using (var scope = app.Services.CreateScope())
{
    var cadastroDb = scope.ServiceProvider.GetRequiredService<CadastroDbContext>();
    if (cadastroDb.Database.IsRelational())
        await cadastroDb.Database.MigrateAsync();

    var pecasInsumoDb = scope.ServiceProvider.GetRequiredService<PecasInsumosDbContext>();
    if (pecasInsumoDb.Database.IsRelational())
        await pecasInsumoDb.Database.MigrateAsync();

    var ordensServicoDb = scope.ServiceProvider.GetRequiredService<OrdensServicoDbContext>();
    if (ordensServicoDb.Database.IsRelational())
        await ordensServicoDb.Database.MigrateAsync();
}

await app.RunAsync();

// Necessário para o WebApplicationFactory<Program> nos testes de integração
public partial class Program { }
