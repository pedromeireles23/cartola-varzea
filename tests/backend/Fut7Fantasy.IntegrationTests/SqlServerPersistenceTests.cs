using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Corte vertical completo: API -> Application -> EF Core -> SQL Server.</summary>
public sealed class SqlServerPersistenceTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly Uri SystemInfo = new("/api/v1/system/info", UriKind.Relative);
    private static readonly Uri Readiness = new("/health/ready", UriKind.Relative);

    [Fact]
    public async Task StartupIsPersistedAndReadBackFromSqlServer()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        using var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<JsonElement>(SystemInfo, cancellationToken);

        // O StartupRecorder gravou ao subir; a consulta le a mesma linha de volta.
        Assert.True(payload.GetProperty("startupCount").GetInt32() >= 1);
        Assert.NotEqual(JsonValueKind.Null, payload.GetProperty("lastStartedAt").ValueKind);
    }

    [Fact]
    public async Task ReadinessIsHealthyWhenDatabaseAnswers()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Readiness, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
