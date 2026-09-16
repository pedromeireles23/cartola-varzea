using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Fut7Fantasy.IntegrationTests;

public sealed class ProblemDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ThrowingPath = "/__tests/throw";
    private const string InternalDetail = "detalhe interno que não pode vazar";

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/rota-inexistente", UriKind.Relative), cancellationToken);
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
        using var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointFilter>());
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

    private sealed class ThrowingEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.Map(ThrowingPath, branch => branch.Run(_ => throw new InvalidOperationException(InternalDetail)));
            };
    }
}
