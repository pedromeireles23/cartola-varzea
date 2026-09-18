using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

public sealed class MatchSheetFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task AssistantSavesAConsistentSheetWithVersionAndAudit()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var ownerEmail = UniqueEmail("sheet-owner");
        var assistantEmail = UniqueEmail("sheet-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Súmulas");
        await AddAssistantAsync(factory, organizationId, assistantId);

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken);
        clock.Advance(TimeSpan.FromHours(3));

        var pendingReview = await assistant.GetFromJsonAsync<JsonElement>(
            Review(world.CompetitionId, world.RoundId), cancellationToken);
        Assert.False(pendingReview.GetProperty("ready").GetBoolean());
        Assert.Contains(pendingReview.GetProperty("pending").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "sheet_missing");
        using var blockedReview = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, world.RoundId),
            new { transition = "SendToReview", version = Version(pendingReview) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, blockedReview.StatusCode);

        var initial = await assistant.GetFromJsonAsync<JsonElement>(
            Sheet(world.CompetitionId, world.MatchId), cancellationToken);
        Assert.Null(initial.GetProperty("version").GetString());
        Assert.Equal(4, initial.GetProperty("athletes").GetArrayLength());

        var appearances = initial.GetProperty("athletes").EnumerateArray().Select(athlete => new
        {
            athleteId = athlete.GetProperty("athleteId").GetGuid(),
            didPlay = true,
            playedAsGoalkeeper = athlete.GetProperty("position").GetString() == "Goalkeeper",
            goalsConceded = 0,
            goals = athlete.GetProperty("athleteId").GetGuid() == world.HomeForward ? 2
                : athlete.GetProperty("athleteId").GetGuid() == world.AwayForward ? 1 : 0,
            assists = 0,
            goalkeeperSaves = 0,
            penaltySaves = 0,
            yellowCards = 0,
            redCards = 0,
            redCardReason = (string?)null,
            ownGoals = 0,
            penaltyMisses = 0,
        }).ToArray();

        var assistantSave = assistant.PutAsJsonAsync(
            Sheet(world.CompetitionId, world.MatchId),
            new { homeScore = 2, awayScore = 1, appearances, version = (string?)null },
            cancellationToken);
        var ownerSave = owner.PutAsJsonAsync(
            Sheet(world.CompetitionId, world.MatchId),
            new { homeScore = 2, awayScore = 1, appearances, version = (string?)null },
            cancellationToken);
        using var assistantResponse = await assistantSave;
        using var ownerResponse = await ownerSave;
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            new[] { assistantResponse.StatusCode, ownerResponse.StatusCode }.Order());
        var savedResponse = assistantResponse.StatusCode == HttpStatusCode.OK
            ? assistantResponse
            : ownerResponse;
        var saved = await savedResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(saved.GetProperty("version").GetString()));
        Assert.Contains(saved.GetProperty("athletes").EnumerateArray(), athlete =>
            athlete.GetProperty("athleteId").GetGuid() == world.HomeGoalkeeper
            && athlete.GetProperty("goalsConceded").GetInt32() == 1);

        var review = await assistant.GetFromJsonAsync<JsonElement>(
            Review(world.CompetitionId, world.RoundId), cancellationToken);
        Assert.True(review.GetProperty("ready").GetBoolean());
        Assert.Equal(1, review.GetProperty("scheduledMatches").GetInt32());
        Assert.Equal(1, review.GetProperty("completedSheets").GetInt32());
        var reviewedMatch = review.GetProperty("matches")[0];
        Assert.Equal(2, reviewedMatch.GetProperty("homeScore").GetInt32());
        Assert.Equal(4, reviewedMatch.GetProperty("participants").GetInt32());
        Assert.Contains(reviewedMatch.GetProperty("events").EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "Goal"
            && item.GetProperty("quantity").GetInt32() == 3);

        using var forbidden = await assistant.PutAsJsonAsync(
            Status(world.CompetitionId, world.RoundId),
            new { transition = "SendToReview", version = Version(review) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var submitted = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, world.RoundId),
            new { transition = "SendToReview", version = Version(review) },
            cancellationToken);
        submitted.EnsureSuccessStatusCode();
        var underReview = await submitted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("UnderReview", underReview.GetProperty("phase").GetString());

        // Mesmo um dado legado ou corrompido fora da API volta a passar pela regra de
        // domínio na revisão; a tela não confia apenas no fato de a súmula existir.
        await using (var mutationScope = factory.Services.CreateAsyncScope())
        {
            var mutationDb = mutationScope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            var goal = await mutationDb.StatEvents.SingleAsync(
                item => item.CompetitionId == world.CompetitionId
                    && item.AthleteId == world.HomeForward,
                cancellationToken);
            mutationDb.Entry(goal).Property(item => item.Quantity).CurrentValue = 9;
            await mutationDb.SaveChangesAsync(cancellationToken);
        }

        var invalidReview = await assistant.GetFromJsonAsync<JsonElement>(
            Review(world.CompetitionId, world.RoundId), cancellationToken);
        Assert.False(invalidReview.GetProperty("ready").GetBoolean());
        Assert.Contains(invalidReview.GetProperty("pending").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "sheet_invalid");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(2, await dbContext.StatEvents.CountAsync(
            item => item.CompetitionId == world.CompetitionId, cancellationToken));
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            item => item.Action == "CompetitionMatchSheetSaved", cancellationToken));
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            item => item.Action == "CompetitionRoundUnderReview", cancellationToken));
    }

    private static async Task<World> BuildAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var competitionResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new
            {
                name = "Copa da Súmula",
                season = "2026",
                modality = "Fut7",
                marketCloseLeadTimeMinutes = 60,
            },
            cancellationToken);
        competitionResponse.EnsureSuccessStatusCode();
        var competition = await competitionResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var competitionId = Id(competition);
        var home = await CreateTeamAsync(owner, competitionId, "Aurora", cancellationToken);
        var away = await CreateTeamAsync(owner, competitionId, "Estrela", cancellationToken);

        using var stageResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages",
            new { name = "Grupo único", format = "Groups", groups = new[] { new { name = "Grupo A" } } },
            cancellationToken);
        stageResponse.EnsureSuccessStatusCode();
        var stage = await stageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var groupId = stage.GetProperty("groups")[0].GetProperty("id").GetGuid();
        using var participants = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages/{Id(stage)}/participants",
            new
            {
                participants = new[]
                {
                    new { realTeamId = home, stageGroupId = groupId },
                    new { realTeamId = away, stageGroupId = groupId },
                },
                version = Version(stage),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        var homeGoalkeeper = await CreateAthleteAsync(
            owner, competitionId, home, "Ana", "Goalkeeper", cancellationToken);
        var homeForward = await CreateAthleteAsync(
            owner, competitionId, home, "Bia", "Forward", cancellationToken);
        await CreateAthleteAsync(owner, competitionId, away, "Cris", "Goalkeeper", cancellationToken);
        var awayForward = await CreateAthleteAsync(
            owner, competitionId, away, "Dani", "Forward", cancellationToken);

        using var roundResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds",
            new { name = "Rodada 1" },
            cancellationToken);
        roundResponse.EnsureSuccessStatusCode();
        var round = await roundResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var kickoffLocal = ApiFactory.FixedNow.AddHours(2).ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        using var matchResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/matches",
            new
            {
                stageId = Id(stage),
                homeTeamId = home,
                awayTeamId = away,
                kickoffLocal,
                version = Version(round),
            },
            cancellationToken);
        matchResponse.EnsureSuccessStatusCode();
        var withMatch = await matchResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var matchId = withMatch.GetProperty("matches")[0].GetProperty("id").GetGuid();
        using var opened = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/status",
            new { transition = "OpenMarket", version = Version(withMatch) },
            cancellationToken);
        opened.EnsureSuccessStatusCode();
        return new(competitionId, Id(round), matchId, homeGoalkeeper, homeForward, awayForward);
    }

    private static async Task<Guid> CreateTeamAsync(
        HttpClient client,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/teams", new { name }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Id(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static async Task<Guid> CreateAthleteAsync(
        HttpClient client,
        Guid competitionId,
        Guid teamId,
        string name,
        string position,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/athletes",
            new
            {
                sportingName = name,
                position,
                realTeamId = teamId,
                priceTier = "Regular",
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return Id(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static string Sheet(Guid competitionId, Guid matchId) =>
        $"/api/v1/competitions/{competitionId}/matches/{matchId}/sheet";

    private static string Review(Guid competitionId, Guid roundId) =>
        $"/api/v1/competitions/{competitionId}/rounds/{roundId}/review";

    private static string Status(Guid competitionId, Guid roundId) =>
        $"/api/v1/competitions/{competitionId}/rounds/{roundId}/status";

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string? Version(JsonElement item) => item.GetProperty("version").GetString();

    private sealed record World(
        Guid CompetitionId,
        Guid RoundId,
        Guid MatchId,
        Guid HomeGoalkeeper,
        Guid HomeForward,
        Guid AwayForward);
}
