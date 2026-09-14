using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fut7Fantasy.IntegrationTests;

public sealed class OpenApiExposureTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Uri OpenApiDocument = new("/openapi/v1.json", UriKind.Relative);

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApiDocumentIsOnlyExposedInDevelopment(string environment, HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment))
            .CreateClient();

        using var response = await client.GetAsync(OpenApiDocument, cancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }
}
