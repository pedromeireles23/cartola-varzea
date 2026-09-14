using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Falhas inesperadas e respostas de erro sem corpo viram Problem Details, sem detalhes internos.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health/live");

await app.RunAsync();

/// <summary>Ponto de entrada exposto para testes de integração.</summary>
public partial class Program;
