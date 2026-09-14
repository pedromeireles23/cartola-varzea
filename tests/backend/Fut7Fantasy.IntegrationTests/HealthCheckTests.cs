using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fut7Fantasy.IntegrationTests;

public sealed class HealthCheckTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LivenessEndpointReturnsHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
