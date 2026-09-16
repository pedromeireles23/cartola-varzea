using System.Net;

namespace Fut7Fantasy.IntegrationTests;

public sealed class HealthCheckTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // A ApiFactory aponta para um SQL Server inexistente de proposito: liveness
    // responde pelo processo e nao pode depender de dependencia externa.
    [Fact]
    public async Task LivenessStaysHealthyWhenDatabaseIsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
