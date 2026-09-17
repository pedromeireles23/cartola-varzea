using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Atletas: inscrição, preço, isolamento, concorrência e desligamento.</summary>
public sealed class AthleteFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task OwnerManagesAthletesWhileAssistantOnlyReadsAndReleasePreservesHistory()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("athletes-owner");
        var assistantEmail = UniqueEmail("athletes-assistant");
        var outsiderEmail = UniqueEmail("athletes-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga dos Atletas");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, outsiderId, "Liga Visitante");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);
        var firstTeam = await CreateTeamAsync(owner, competitionId, "Aurora", cancellationToken);
        var secondTeam = await CreateTeamAsync(owner, competitionId, "Estrela", cancellationToken);
        var athlete = await CreateAthleteAsync(
            owner,
            competitionId,
            firstTeam,
            "  Bia  ",
            "Midfielder",
            "Star",
            null,
            cancellationToken);

        Assert.Equal("Bia", athlete.GetProperty("sportingName").GetString());
        Assert.Equal(11m, athlete.GetProperty("initialPrice").GetDecimal());
        Assert.True(athlete.GetProperty("isAvailable").GetBoolean());

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantList = await assistant.GetFromJsonAsync<JsonElement>(
            AthletesOf(competitionId), cancellationToken);
        Assert.Equal("Bia", assistantList[0].GetProperty("sportingName").GetString());
        using var assistantCreate = await assistant.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(firstTeam, "Sem permissão", "Forward", "Regular", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantCreate.StatusCode);

        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        using var outsiderList = await outsider.GetAsync(AthletesOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderList.StatusCode);

        using var transfer = await owner.PutAsJsonAsync(
            AthleteOf(competitionId, athlete),
            Request(secondTeam, "Bia", "Midfielder", "Star", null, Version(athlete)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);
        Assert.Equal("athlete_transfer_not_allowed", await ProblemCodeAsync(transfer, cancellationToken));

        using var updatedResponse = await owner.PutAsJsonAsync(
            AthleteOf(competitionId, athlete),
            Request(firstTeam, "Beatriz", "Midfielder", "Basic", 6.25m, Version(athlete)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(6.25m, updated.GetProperty("initialPrice").GetDecimal());

        using var released = await owner.DeleteAsync(
            AthleteOf(competitionId, updated), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        var finalList = await owner.GetFromJsonAsync<JsonElement>(
            AthletesOf(competitionId), cancellationToken);
        Assert.Single(finalList.EnumerateArray());
        Assert.Equal("Released", finalList[0].GetProperty("status").GetString());
        Assert.False(finalList[0].GetProperty("isAvailable").GetBoolean());
        Assert.Equal(6.25m, finalList[0].GetProperty("initialPrice").GetDecimal());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(1, await dbContext.Athletes.CountAsync(
            item => item.CompetitionId == competitionId, cancellationToken));
        Assert.Equal(1, await dbContext.RosterRegistrations.CountAsync(
            item => item.CompetitionId == competitionId, cancellationToken));
        Assert.Equal(3, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.ActorUserId == ownerId && entry.Action.StartsWith("Athlete"),
            cancellationToken));
    }

    [Fact]
    public async Task ValidationVersionAndConcurrentDuplicateAreEnforced()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("athletes-race-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Concorrida");
        using var firstClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var secondClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(firstClient, organizationId, cancellationToken);
        var teamId = await CreateTeamAsync(firstClient, competitionId, "Aurora", cancellationToken);

        using var invalid = await firstClient.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(teamId, "x", "Goalkeeper", "Regular", 31m),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await ValidationErrorsAsync(invalid, cancellationToken);
        Assert.Contains("SportingName", errors);
        Assert.Contains("InitialPriceOverride", errors);

        using var invalidEnum = await firstClient.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(teamId, "Bia", "Ala", "Craque", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEnum.StatusCode);
        var enumErrors = await ValidationErrorsAsync(invalidEnum, cancellationToken);
        Assert.Contains("Position", enumErrors);
        Assert.Contains("PriceTier", enumErrors);

        var firstCreate = firstClient.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(teamId, "Caio", "Forward", "Regular", null),
            cancellationToken);
        var secondCreate = secondClient.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(teamId, "Caio", "Forward", "Regular", null),
            cancellationToken);
        using var firstResponse = await firstCreate;
        using var secondResponse = await secondCreate;
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            new[] { firstResponse.StatusCode, secondResponse.StatusCode }.Order());

        var createdResponse = firstResponse.StatusCode == HttpStatusCode.Created
            ? firstResponse
            : secondResponse;
        var athlete = await createdResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        using var missingVersion = await firstClient.PutAsJsonAsync(
            AthleteOf(competitionId, athlete),
            Request(teamId, "Caio Silva", "Forward", "Star", null),
            cancellationToken);
        Assert.Contains("Version", await ValidationErrorsAsync(missingVersion, cancellationToken));

        using var updated = await firstClient.PutAsJsonAsync(
            AthleteOf(competitionId, athlete),
            Request(teamId, "Caio Silva", "Forward", "Star", null, Version(athlete)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var stale = await secondClient.PutAsJsonAsync(
            AthleteOf(competitionId, athlete),
            Request(teamId, "Leitura antiga", "Forward", "Basic", null, Version(athlete)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient client,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = "Copa dos Atletas", season = "2026", modality = "Fut7" },
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

    private static async Task<JsonElement> CreateAthleteAsync(
        HttpClient client,
        Guid competitionId,
        Guid teamId,
        string sportingName,
        string position,
        string priceTier,
        decimal? initialPriceOverride,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            AthletesOf(competitionId),
            Request(teamId, sportingName, position, priceTier, initialPriceOverride),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static object Request(
        Guid teamId,
        string sportingName,
        string position,
        string priceTier,
        decimal? initialPriceOverride,
        string? version = null) =>
        new { sportingName, position, realTeamId = teamId, priceTier, initialPriceOverride, version };

    private static string AthletesOf(Guid competitionId) =>
        $"/api/v1/competitions/{competitionId}/athletes";

    private static string AthleteOf(Guid competitionId, JsonElement athlete) =>
        $"{AthletesOf(competitionId)}/{athlete.GetProperty("id").GetGuid()}";

    private static string Version(JsonElement athlete) => athlete.GetProperty("version").GetString()!;

    private static async Task<string[]> ValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    private static async Task<string?> ProblemCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return problem.GetProperty("code").GetString();
    }
}
