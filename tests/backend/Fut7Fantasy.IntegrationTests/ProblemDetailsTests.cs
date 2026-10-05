using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fut7Fantasy.IntegrationTests;

public sealed class ProblemDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ThrowingPath = "/api/v1/system/info";
    private const string InternalDetail = "detalhe interno que não pode vazar";

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        // Fora de /api, rota desconhecida é do SPA e recebe o index.html (SpaHostingTests).
        using var response = await client.GetAsync(
            new Uri("/api/v1/rota-inexistente", UriKind.Relative), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnhandledExceptionInProductionReturnsProblemDetailsWithoutInternalDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A falha nasce dentro de um endpoint de verdade: uma rota pendurada depois do
        // pipeline nunca seria alcançada, porque o fallback do SPA atende antes.
        using var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IStartupLog>();
                    services.AddSingleton<IStartupLog, ThrowingStartupLog>();
                });
            })
            .CreateClient();

        using var response = await client.GetAsync(new Uri(ThrowingPath, UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(InternalDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }

    private sealed class ThrowingStartupLog : IStartupLog
    {
        public Task RecordAsync(string version, string environmentName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<StartupLogSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(InternalDetail);
    }
}
