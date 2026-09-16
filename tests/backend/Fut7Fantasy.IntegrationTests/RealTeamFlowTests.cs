using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Times reais: escopo, validação, concorrência e preservação por arquivamento.</summary>
public sealed class RealTeamFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task OwnerManagesTeamsWhileAssistantOnlyReadsAndOutsiderSeesNothing()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("teams-owner");
        var assistantEmail = UniqueEmail("teams-assistant");
        var outsiderEmail = UniqueEmail("teams-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga dos Times");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, outsiderId, "Liga de Fora");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var estrela = await CreateTeamAsync(owner, competitionId, "  Estrela da Vila  ", cancellationToken);
        var unidos = await CreateTeamAsync(owner, competitionId, "Unidos", cancellationToken);
        Assert.Equal("Estrela da Vila", estrela.GetProperty("name").GetString());

        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantList = await assistant.GetFromJsonAsync<JsonElement>(TeamsOf(competitionId), cancellationToken);
        Assert.Equal(["Estrela da Vila", "Unidos"], Names(assistantList));
        using var assistantCreate = await assistant.PostAsJsonAsync(
            TeamsOf(competitionId), new { name = "Sem permissão" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantCreate.StatusCode);
        using var assistantUpdate = await assistant.PutAsJsonAsync(
            TeamOf(competitionId, estrela),
            new { name = "Sem permissão", version = Version(estrela) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantUpdate.StatusCode);
        using var assistantArchive = await assistant.DeleteAsync(
            TeamOf(competitionId, estrela), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantArchive.StatusCode);

        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        using var outsiderList = await outsider.GetAsync(TeamsOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderList.StatusCode);

        using var updatedResponse = await owner.PutAsJsonAsync(
            TeamOf(competitionId, estrela),
            new { name = "Estrela FC", version = Version(estrela) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Estrela FC", updated.GetProperty("name").GetString());

        using var duplicate = await owner.PutAsJsonAsync(
            TeamOf(competitionId, updated),
            new { name = "unidos", version = Version(updated) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("real_team_name_duplicate", await ProblemCodeAsync(duplicate, cancellationToken));

        using var archived = await owner.DeleteAsync(TeamOf(competitionId, unidos), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        var finalList = await owner.GetFromJsonAsync<JsonElement>(TeamsOf(competitionId), cancellationToken);
        Assert.Equal(["Estrela FC", "Unidos"], Names(finalList));
        Assert.False(finalList[0].GetProperty("isArchived").GetBoolean());
        Assert.True(finalList[1].GetProperty("isArchived").GetBoolean());

        using var editArchived = await owner.PutAsJsonAsync(
            TeamOf(competitionId, unidos),
            new { name = "Não volta", version = Version(unidos) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, editArchived.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(2, await dbContext.RealTeams.CountAsync(
            team => team.CompetitionId == competitionId, cancellationToken));
        Assert.Equal(4, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.ActorUserId == ownerId && entry.Action.StartsWith("RealTeam"),
            cancellationToken));
    }

    [Fact]
    public async Task ValidationVersionAndConcurrentDuplicateAreEnforced()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("teams-race-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Concorrida");
        using var firstClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var secondClient = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(firstClient, organizationId, cancellationToken);

        using var invalid = await firstClient.PostAsJsonAsync(
            TeamsOf(competitionId), new { name = "x" }, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Name", await ValidationErrorsAsync(invalid, cancellationToken));

        var firstCreate = firstClient.PostAsJsonAsync(
            TeamsOf(competitionId), new { name = "Rivais" }, cancellationToken);
        var secondCreate = secondClient.PostAsJsonAsync(
            TeamsOf(competitionId), new { name = "Rivais" }, cancellationToken);
        using var firstResponse = await firstCreate;
        using var secondResponse = await secondCreate;
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            new[] { firstResponse.StatusCode, secondResponse.StatusCode }.Order());

        var createdResponse = firstResponse.StatusCode == HttpStatusCode.Created ? firstResponse : secondResponse;
        var team = await createdResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        using var missingVersion = await firstClient.PutAsJsonAsync(
            TeamOf(competitionId, team), new { name = "Rivais FC" }, cancellationToken);
        Assert.Contains("Version", await ValidationErrorsAsync(missingVersion, cancellationToken));

        using var updatedResponse = await firstClient.PutAsJsonAsync(
            TeamOf(competitionId, team),
            new { name = "Rivais FC", version = Version(team) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);

        using var stale = await secondClient.PutAsJsonAsync(
            TeamOf(competitionId, team),
            new { name = "Leitura antiga", version = Version(team) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Single(await dbContext.RealTeams
            .Where(item => item.CompetitionId == competitionId)
            .ToListAsync(cancellationToken));
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient client,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = "Copa dos Times", season = "2026", modality = "Fut7" },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var competition = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return competition.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateTeamAsync(
        HttpClient client,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            TeamsOf(competitionId), new { name }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static string TeamsOf(Guid competitionId) => $"/api/v1/competitions/{competitionId}/teams";

    private static string TeamOf(Guid competitionId, JsonElement team) =>
        $"{TeamsOf(competitionId)}/{team.GetProperty("id").GetGuid()}";

    private static string Version(JsonElement team) => team.GetProperty("version").GetString()!;

    private static string[] Names(JsonElement teams) =>
        [.. teams.EnumerateArray().Select(team => team.GetProperty("name").GetString()!)];

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
