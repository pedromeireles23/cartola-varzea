using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Times por fase, distribuição em grupos, avanço manual e concorrência.</summary>
public sealed class StageParticipantFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly string[] Wins = ["Wins"];

    [Fact]
    public async Task OwnerDistributesTeamsAndAssistantReadsManualProgression()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("participants-owner");
        var assistantEmail = UniqueEmail("participants-assistant");
        var outsiderEmail = UniqueEmail("participants-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Participantes");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, outsiderId, "Liga Externa");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);
        var alpha = await CreateTeamAsync(owner, competitionId, "Alpha", cancellationToken);
        var beta = await CreateTeamAsync(owner, competitionId, "Beta", cancellationToken);
        var archived = await CreateTeamAsync(owner, competitionId, "Arquivado", cancellationToken);
        using (var archive = await owner.DeleteAsync(TeamOf(competitionId, Id(archived)), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        }

        var groups = await CreateGroupStageAsync(owner, competitionId, cancellationToken);
        var groupA = GroupId(groups, 0);
        var groupB = GroupId(groups, 1);
        using var distributed = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, groups),
            new
            {
                participants = new[]
                {
                    new { realTeamId = Id(alpha), stageGroupId = groupA },
                    new { realTeamId = Id(beta), stageGroupId = groupB },
                },
                version = Version(groups),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, distributed.StatusCode);
        var current = await distributed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.NotEqual(Version(groups), Version(current));
        Assert.Equal(
            [("Alpha", "Grupo A"), ("Beta", "Grupo B")],
            Participants(current).Select(participant => (
                participant.GetProperty("realTeamName").GetString(),
                participant.GetProperty("stageGroupName").GetString())));

        // Auxiliar acompanha a distribuição, mas somente o proprietário confirma mudanças.
        var assistantStages = await assistant.GetFromJsonAsync<JsonElement>(StagesOf(competitionId), cancellationToken);
        Assert.Equal(2, Participants(assistantStages[0]).Count());
        using var assistantWrite = await assistant.PutAsJsonAsync(
            ParticipantsOf(competitionId, current),
            new { participants = Array.Empty<object>(), version = Version(current) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantWrite.StatusCode);
        using var outsiderRead = await outsider.GetAsync(StagesOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRead.StatusCode);

        using var archivedTeam = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, current),
            new
            {
                participants = new[]
                {
                    new { realTeamId = Id(alpha), stageGroupId = groupA },
                    new { realTeamId = Id(beta), stageGroupId = groupB },
                    new { realTeamId = Id(archived), stageGroupId = groupA },
                },
                version = Version(current),
            },
            cancellationToken);
        Assert.Contains("Participants", await ValidationErrorsAsync(archivedTeam, cancellationToken));

        // A mesma equipe pode avançar manualmente para uma fase seguinte de mata-mata.
        var knockout = await CreateKnockoutStageAsync(owner, competitionId, cancellationToken);
        using var progressed = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, knockout),
            new
            {
                participants = new[]
                {
                    new { realTeamId = Id(alpha), stageGroupId = (Guid?)null },
                    new { realTeamId = Id(beta), stageGroupId = (Guid?)null },
                },
                version = Version(knockout),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, progressed.StatusCode);
        var knockoutCurrent = await progressed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.All(Participants(knockoutCurrent), participant =>
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("stageGroupId").ValueKind));

        using var groupInKnockout = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, knockoutCurrent),
            new
            {
                participants = new[] { new { realTeamId = Id(alpha), stageGroupId = groupA } },
                version = Version(knockoutCurrent),
            },
            cancellationToken);
        Assert.Contains("Participants", await ValidationErrorsAsync(groupInKnockout, cancellationToken));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var stageIds = new[] { Id(groups), Id(knockout) };
        Assert.Equal(4, await dbContext.StageParticipants.CountAsync(
            participant => stageIds.Contains(participant.StageId),
            cancellationToken));
        Assert.Equal(2, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionStageParticipantsUpdated"
                && (entry.TargetId == Id(groups) || entry.TargetId == Id(knockout)),
            cancellationToken));
    }

    [Fact]
    public async Task ParticipantsProtectVersionFormatAndUsedGroups()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("participants-guard-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Protegida");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);
        var team = await CreateTeamAsync(owner, competitionId, "Time Único", cancellationToken);
        var stage = await CreateGroupStageAsync(owner, competitionId, cancellationToken);
        var groupA = GroupId(stage, 0);

        using var assigned = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, stage),
            new
            {
                participants = new[] { new { realTeamId = Id(team), stageGroupId = groupA } },
                version = Version(stage),
            },
            cancellationToken);
        assigned.EnsureSuccessStatusCode();
        var current = await assigned.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        using var stale = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, stage),
            new { participants = Array.Empty<object>(), version = Version(stage) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var formatChange = await owner.PutAsJsonAsync(
            StageOf(competitionId, current),
            new
            {
                name = "Mata-mata",
                format = "Knockout",
                groups = Array.Empty<object>(),
                tiebreakers = Array.Empty<string>(),
                version = Version(current),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, formatChange.StatusCode);
        Assert.Equal("competition_stage_dependencies", await ProblemCodeAsync(formatChange, cancellationToken));

        using var removeUsedGroup = await owner.PutAsJsonAsync(
            StageOf(competitionId, current),
            new
            {
                name = "Grupos",
                format = "Groups",
                groups = new[] { new { id = GroupId(current, 1), name = "Grupo B" } },
                tiebreakers = Wins,
                version = Version(current),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, removeUsedGroup.StatusCode);
        Assert.Equal("competition_stage_dependencies", await ProblemCodeAsync(removeUsedGroup, cancellationToken));

        // Remover a fase é a operação explícita que também remove suas associações.
        using var deleted = await owner.DeleteAsync(StageOf(competitionId, current), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var deletedStageId = Id(current);
        Assert.False(await dbContext.StageParticipants.AnyAsync(
            participant => participant.StageId == deletedStageId,
            cancellationToken));
    }

    private static Uri StagesOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/stages", UriKind.Relative);

    private static Uri StageOf(Guid competitionId, JsonElement stage) =>
        new($"/api/v1/competitions/{competitionId}/stages/{Id(stage)}", UriKind.Relative);

    private static Uri ParticipantsOf(Guid competitionId, JsonElement stage) =>
        new($"/api/v1/competitions/{competitionId}/stages/{Id(stage)}/participants", UriKind.Relative);

    private static Uri TeamsOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/teams", UriKind.Relative);

    private static Uri TeamOf(Guid competitionId, Guid teamId) =>
        new($"/api/v1/competitions/{competitionId}/teams/{teamId}", UriKind.Relative);

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string? Version(JsonElement item) => item.GetProperty("version").GetString();

    private static Guid GroupId(JsonElement stage, int index) =>
        stage.GetProperty("groups")[index].GetProperty("id").GetGuid();

    private static JsonElement.ArrayEnumerator Participants(JsonElement stage) =>
        stage.GetProperty("participants").EnumerateArray();

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name = "Copa com Participantes", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return Id(await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static async Task<JsonElement> CreateTeamAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            TeamsOf(competitionId), new { name }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<JsonElement> CreateGroupStageAsync(
        HttpClient owner,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            StagesOf(competitionId),
            new
            {
                name = "Fase de grupos",
                format = "Groups",
                groups = new[] { new { name = "Grupo A" }, new { name = "Grupo B" } },
            },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<JsonElement> CreateKnockoutStageAsync(
        HttpClient owner,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Semifinal", format = "Knockout" }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<string[]> ValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
