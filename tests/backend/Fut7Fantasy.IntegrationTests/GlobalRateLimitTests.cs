using System.Net;

namespace Fut7Fantasy.IntegrationTests;

public sealed class GlobalRateLimitTests
{
    [Fact]
    public async Task GlobalLimitBlocksTheSameIpAfterProductionQuota()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        // O valor de produção vem do ApiFactory, não do appsettings.Development.json.
        for (var request = 0; request < 300; request++)
        {
            using var allowed = await client.GetAsync(
                new Uri("/health/live", UriKind.Relative), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
