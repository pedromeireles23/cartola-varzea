using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Técnico único: criação com o time, fallback, preço, isolamento e concorrência.</summary>
public sealed class CoachFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task TeamCreatesOneCoachAndOwnerEditsWhileAssistantOnlyReads()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("coaches-owner");
        var assistantEmail = UniqueEmail("coaches-assistant");
        var outsiderEmail = UniqueEmail("coaches-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga dos Técnicos");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, outsiderId, "Liga Visitante");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);
        var team = await CreateTeamAsync(owner, competitionId, "União da Vila", cancellationToken);

        var coaches = await owner.GetFromJsonAsync<JsonElement>(CoachesOf(competitionId), cancellationToken);
        var coach = Assert.Single(coaches.EnumerateArray());
        Assert.Equal(team, coach.GetProperty("realTeamId").GetGuid());
        Assert.Equal(JsonValueKind.Null, coach.GetProperty("displayName").ValueKind);
        Assert.Equal("Técnico do União da Vila", coach.GetProperty("effectiveName").GetString());
        Assert.Equal("Regular", coach.GetProperty("priceTier").GetString());
        Assert.Equal(8m, coach.GetProperty("initialPrice").GetDecimal());
        Assert.True(coach.GetProperty("isAvailable").GetBoolean());

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantList = await assistant.GetFromJsonAsync<JsonElement>(
            CoachesOf(competitionId), cancellationToken);
        Assert.Equal("Técnico do União da Vila", assistantList[0].GetProperty("effectiveName").GetString());
        using var assistantUpdate = await assistant.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request("Sem permissão", "Star", null, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantUpdate.StatusCode);

        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        using var outsiderList = await outsider.GetAsync(CoachesOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderList.StatusCode);

        using var updatedResponse = await owner.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request("  Professor Beto  ", "Star", null, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Professor Beto", updated.GetProperty("displayName").GetString());
        Assert.Equal("Professor Beto", updated.GetProperty("effectiveName").GetString());
        Assert.Equal(11m, updated.GetProperty("initialPrice").GetDecimal());

        using var fallbackResponse = await owner.PutAsJsonAsync(
            CoachOf(competitionId, updated),
            Request("  ", "Basic", 6.25m, Version(updated)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, fallbackResponse.StatusCode);
        var fallback = await fallbackResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(JsonValueKind.Null, fallback.GetProperty("displayName").ValueKind);
        Assert.Equal("Técnico do União da Vila", fallback.GetProperty("effectiveName").GetString());
        Assert.Equal(6.25m, fallback.GetProperty("initialPrice").GetDecimal());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Single(await dbContext.Coaches
            .Where(item => item.CompetitionId == competitionId && item.RealTeamId == team)
            .ToListAsync(cancellationToken));
        Assert.Equal(3, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.ActorUserId == ownerId && entry.Action.StartsWith("Coach"),
            cancellationToken));
    }

    [Fact]
    public async Task ValidationConcurrencyAndArchivedTeamAreEnforced()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("coaches-version-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga dos Bancos");
        using var firstClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var secondClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(firstClient, organizationId, cancellationToken);
        var team = await CreateTeamAsync(firstClient, competitionId, "Aurora", cancellationToken);
        var coaches = await firstClient.GetFromJsonAsync<JsonElement>(
            CoachesOf(competitionId), cancellationToken);
        var coach = Assert.Single(coaches.EnumerateArray());

        using var invalid = await firstClient.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request("x", "Regular", 31m, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await ValidationErrorsAsync(invalid, cancellationToken);
        Assert.Contains("DisplayName", errors);
        Assert.Contains("InitialPriceOverride", errors);

        using var invalidTier = await firstClient.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request(null, "Craque", null, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTier.StatusCode);
        Assert.Contains("PriceTier", await ValidationErrorsAsync(invalidTier, cancellationToken));

        using var missingVersion = await firstClient.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request(null, "Regular", null, null),
            cancellationToken);
        Assert.Contains("Version", await ValidationErrorsAsync(missingVersion, cancellationToken));

        using var updatedResponse = await firstClient.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request("Professor Lia", "Regular", null, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);

        using var stale = await secondClient.PutAsJsonAsync(
            CoachOf(competitionId, coach),
            Request("Leitura antiga", "Star", null, Version(coach)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var archive = await firstClient.DeleteAsync(
            $"/api/v1/competitions/{competitionId}/teams/{team}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        var finalList = await firstClient.GetFromJsonAsync<JsonElement>(
            CoachesOf(competitionId), cancellationToken);
        Assert.False(finalList[0].GetProperty("isAvailable").GetBoolean());

        var current = finalList[0];
        using var editArchived = await firstClient.PutAsJsonAsync(
            CoachOf(competitionId, current),
            Request("Não altera", "Regular", null, Version(current)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, editArchived.StatusCode);
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient client,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = "Copa dos Técnicos", season = "2026", modality = "Fut7" },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var competition = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return competition.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTeamAsync(
        HttpClient client,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/teams",
            new { name },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var team = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return team.GetProperty("id").GetGuid();
    }

    private static object Request(
        string? displayName,
        string priceTier,
        decimal? initialPriceOverride,
        string? version) =>
        new { displayName, priceTier, initialPriceOverride, version };

    private static string CoachesOf(Guid competitionId) =>
        $"/api/v1/competitions/{competitionId}/coaches";

    private static string CoachOf(Guid competitionId, JsonElement coach) =>
        $"{CoachesOf(competitionId)}/{coach.GetProperty("id").GetGuid()}";

    private static string Version(JsonElement coach) => coach.GetProperty("version").GetString()!;

    private static async Task<string[]> ValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }
}
