using System.Diagnostics;
using Fut7Fantasy.Application;
using Fut7Fantasy.Application.Diagnostics;
using Fut7Fantasy.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks().AddInfrastructureHealthChecks();

var app = builder.Build();

// Falhas inesperadas e respostas de erro sem corpo viram Problem Details, sem detalhes internos.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Liveness responde pelo processo e nao consulta dependencia alguma.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness responde pela capacidade de atender, e por isso inclui o banco.
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Readiness) });

app.MapGet(
        "/api/v1/system/info",
        async (GetSystemInfo getSystemInfo, CancellationToken cancellationToken) =>
            await getSystemInfo.ExecuteAsync(cancellationToken))
    .WithName("GetSystemInfo")
    .WithSummary("Informação técnica pública da aplicação.");

await app.RunAsync();

/// <summary>Ponto de entrada exposto para testes de integração.</summary>
public partial class Program;
