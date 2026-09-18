using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Disponibilidade do catálogo que depende das fases e do mercado (Fase 6): eliminação
/// pela confirmação da fase seguinte e posição travada na abertura do mercado.
/// </summary>
public sealed class CatalogAvailabilityFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task TeamLeftOutOfTheNextStageIsEliminatedAndMarketOpeningLocksPositions()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(ApiFactory.FixedNow));
        });
        var ownerEmail = UniqueEmail("availability-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga da Eliminação");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        var competitionId = await PostIdAsync(
            owner,
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = "Copa do Mata-mata", season = "2026", modality = "Fut7" },
            cancellationToken);
        var teams = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in new[] { "Aurora", "Estrela", "Brisa" })
        {
            teams[name] = await PostIdAsync(
                owner, $"/api/v1/competitions/{competitionId}/teams", new { name }, cancellationToken);
        }

        foreach (var (name, team) in new[] { ("Ana", "Aurora"), ("Cris", "Estrela"), ("Duda", "Brisa") })
        {
            await PostIdAsync(
                owner,
                $"/api/v1/competitions/{competitionId}/athletes",
                new { sportingName = name, position = "Forward", realTeamId = teams[team], priceTier = "Regular" },
                cancellationToken);
        }

        var groups = await CreateStageAsync(owner, competitionId, "Grupos", "Groups", cancellationToken);
        await ConfirmAsync(owner, competitionId, groups, ["Aurora", "Estrela", "Brisa"], teams, cancellationToken);
        var final = await CreateStageAsync(owner, competitionId, "Final", "Knockout", cancellationToken);

        // Só uma fase tem times: ninguém caiu ainda, nem com a final criada.
        Assert.All(await AthletesAsync(owner, competitionId, cancellationToken), athlete =>
            Assert.True(athlete.GetProperty("isAvailable").GetBoolean()));

        await ConfirmAsync(owner, competitionId, final, ["Aurora", "Estrela"], teams, cancellationToken);

        var athletes = (await AthletesAsync(owner, competitionId, cancellationToken))
            .ToDictionary(athlete => athlete.GetProperty("sportingName").GetString()!);
        Assert.True(athletes["Duda"].GetProperty("isEliminated").GetBoolean());
        Assert.False(athletes["Duda"].GetProperty("isAvailable").GetBoolean());
        Assert.False(athletes["Ana"].GetProperty("isEliminated").GetBoolean());
        Assert.True(athletes["Ana"].GetProperty("isAvailable").GetBoolean());

        var coaches = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{competitionId}/coaches", cancellationToken);
        var brisaCoach = coaches.EnumerateArray()
            .Single(coach => coach.GetProperty("realTeamName").GetString() == "Brisa");
        Assert.True(brisaCoach.GetProperty("isEliminated").GetBoolean());
        Assert.False(brisaCoach.GetProperty("isAvailable").GetBoolean());

        // Abrir o mercado trava a posição de quem estava disponível, e só de quem estava.
        var round = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds", new { name = "Final" }, cancellationToken);
        round.EnsureSuccessStatusCode();
        var created = await round.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var roundId = created.GetProperty("id").GetGuid();
        using var match = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{roundId}/matches",
            new
            {
                stageId = final.GetProperty("id").GetGuid(),
                homeTeamId = teams["Aurora"],
                awayTeamId = teams["Estrela"],
                kickoffLocal = ApiFactory.FixedNow.AddDays(2).ToOffset(TimeSpan.FromHours(-3))
                    .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                version = created.GetProperty("version").GetString(),
            },
            cancellationToken);
        match.EnsureSuccessStatusCode();
        var withMatch = await match.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var opened = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{roundId}/status",
            new { transition = "OpenMarket", version = withMatch.GetProperty("version").GetString() },
            cancellationToken);
        opened.EnsureSuccessStatusCode();

        athletes = (await AthletesAsync(owner, competitionId, cancellationToken))
            .ToDictionary(athlete => athlete.GetProperty("sportingName").GetString()!);
        using var lockedChange = await ChangePositionAsync(
            owner, competitionId, athletes["Ana"], teams["Aurora"], cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, lockedChange.StatusCode);
        Assert.Contains("athlete_position_locked", await lockedChange.Content.ReadAsStringAsync(cancellationToken));

        using var eliminatedChange = await ChangePositionAsync(
            owner, competitionId, athletes["Duda"], teams["Brisa"], cancellationToken);
        Assert.Equal(HttpStatusCode.OK, eliminatedChange.StatusCode);
    }

    private static Task<HttpResponseMessage> ChangePositionAsync(
        HttpClient owner,
        Guid competitionId,
        JsonElement athlete,
        Guid teamId,
        CancellationToken cancellationToken) =>
        owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/athletes/{athlete.GetProperty("id").GetGuid()}",
            new
            {
                sportingName = athlete.GetProperty("sportingName").GetString(),
                position = "Defender",
                realTeamId = teamId,
                priceTier = "Regular",
                version = athlete.GetProperty("version").GetString(),
            },
            cancellationToken);

    private static async Task<List<JsonElement>> AthletesAsync(
        HttpClient owner,
        Guid competitionId,
        CancellationToken cancellationToken) =>
        [
            .. (await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{competitionId}/athletes", cancellationToken)).EnumerateArray(),
        ];

    private static async Task<JsonElement> CreateStageAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        string format,
        CancellationToken cancellationToken)
    {
        using var response = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages",
            format == "Groups"
                ? new { name, format, groups = new[] { new { name = "Grupo A" } } }
                : (object)new { name, format },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task ConfirmAsync(
        HttpClient owner,
        Guid competitionId,
        JsonElement stage,
        string[] names,
        Dictionary<string, Guid> teams,
        CancellationToken cancellationToken)
    {
        var groupId = stage.GetProperty("groups").EnumerateArray()
            .Select(group => (Guid?)group.GetProperty("id").GetGuid())
            .FirstOrDefault();
        var current = (await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{competitionId}/stages", cancellationToken))
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == stage.GetProperty("id").GetGuid());
        using var response = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages/{stage.GetProperty("id").GetGuid()}/participants",
            new
            {
                participants = names.Select(name => new { realTeamId = teams[name], stageGroupId = groupId }),
                version = current.GetProperty("version").GetString(),
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> PostIdAsync(
        HttpClient client,
        string uri,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(uri, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();
    }
}
