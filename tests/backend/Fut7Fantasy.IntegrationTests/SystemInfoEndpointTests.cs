using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fut7Fantasy.IntegrationTests;

public sealed class SystemInfoEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri SystemInfo = new("/api/v1/system/info", UriKind.Relative);

    [Fact]
    public async Task SystemInfoUsesServerClockAndReportsRecordedStartups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<JsonElement>(SystemInfo, cancellationToken);

        Assert.Equal(ApiFactory.FixedNow, payload.GetProperty("serverTimeUtc").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, payload.GetProperty("serverTimeUtc").GetDateTimeOffset().Offset);
        // O host de teste registra a propria inicializacao pelo StartupRecorder.
        Assert.True(payload.GetProperty("startupCount").GetInt32() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task SystemInfoDoesNotExposeInfrastructureDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(SystemInfo, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<JsonElement>(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["environment", "lastStartedAt", "serverTimeUtc", "startupCount", "version"],
            payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }
}
