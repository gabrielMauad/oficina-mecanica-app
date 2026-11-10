using Api.Extensions;
using Api.Middlewares;
using Api.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OrdensServico.Infrastructure;
using OrdensServico.Infrastructure.Persistence;
using OrdensServico.Web;
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
builder.Services.AddSharedKernelServices();
builder.Services.AddOrdensServicoModule(builder.Configuration);

builder.Services.AddControllers()
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
    var ordensServicoDb = scope.ServiceProvider.GetRequiredService<OrdensServicoDbContext>();
    if (ordensServicoDb.Database.IsRelational())
        await ordensServicoDb.Database.MigrateAsync();
}

await app.RunAsync();

// Necessário para o WebApplicationFactory<Program> nos testes de integração
public partial class Program { }
