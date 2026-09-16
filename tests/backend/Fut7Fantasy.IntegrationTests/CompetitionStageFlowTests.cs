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

/// <summary>Fases do campeonato: permissões, grupos, ordem e concorrência.</summary>
public sealed class CompetitionStageFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly string[] GoalDifferenceThenWins = ["GoalDifference", "Wins"];
    private static readonly string[] UnknownCriterion = ["Luck"];

    [Fact]
    public async Task OwnerManagesStagesAndOnlyTheOrganizationReadsThem()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("stages-owner");
        var assistantEmail = UniqueEmail("stages-assistant");
        var otherOwnerEmail = UniqueEmail("stages-other-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var otherOwnerId = await CreateUserAsync(factory, otherOwnerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Fases");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, otherOwnerId, "Liga Alheia");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        using var groups = await owner.PostAsJsonAsync(
            StagesOf(competitionId),
            new
            {
                name = "Fase de grupos",
                format = "Groups",
                groups = new[] { new { name = "Grupo A" }, new { name = "Grupo B" } },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, groups.StatusCode);
        var groupStage = await groups.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(1, groupStage.GetProperty("sequence").GetInt32());
        Assert.Equal(
            ["Wins", "GoalDifference", "GoalsFor", "HeadToHead", "FewestRedCards", "FewestYellowCards"],
            groupStage.GetProperty("tiebreakers").EnumerateArray().Select(item => item.GetString()));

        using var knockout = await owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Mata-mata", format = "Knockout" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, knockout.StatusCode);
        var knockoutStage = await knockout.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(2, knockoutStage.GetProperty("sequence").GetInt32());
        Assert.Empty(knockoutStage.GetProperty("groups").EnumerateArray());

        // O auxiliar acompanha as fases, mas não cria, altera, remove nem reordena.
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantList = await assistant.GetFromJsonAsync<JsonElement>(StagesOf(competitionId), cancellationToken);
        Assert.Equal(
            ["Fase de grupos", "Mata-mata"],
            assistantList.EnumerateArray().Select(stage => stage.GetProperty("name").GetString()));
        using var assistantCreate = await assistant.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Final", format = "Knockout" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantCreate.StatusCode);
        using var assistantUpdate = await assistant.PutAsJsonAsync(
            StageOf(competitionId, knockoutStage), KnockoutBody(knockoutStage, "Final"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantUpdate.StatusCode);
        using var assistantDelete = await assistant.DeleteAsync(
            StageOf(competitionId, knockoutStage), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantDelete.StatusCode);
        using var assistantReorder = await assistant.PutAsJsonAsync(
            OrderOf(competitionId),
            new { stageIds = new[] { Id(knockoutStage), Id(groupStage) } },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantReorder.StatusCode);

        // Outra organização não lê nem escreve.
        using var otherOwner = await CreateAuthenticatedClientAsync(factory, otherOwnerEmail, cancellationToken);
        using var otherList = await otherOwner.GetAsync(StagesOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherList.StatusCode);
        using var otherCreate = await otherOwner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Invasora", format = "Knockout" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherCreate.StatusCode);
        using var otherDelete = await otherOwner.DeleteAsync(StageOf(competitionId, groupStage), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherDelete.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(2, await dbContext.Stages.CountAsync(
            stage => stage.CompetitionId == competitionId, cancellationToken));
        Assert.Equal(2, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionStageCreated"
                && (entry.TargetId == Id(groupStage) || entry.TargetId == Id(knockoutStage)),
            cancellationToken));
    }

    [Fact]
    public async Task UpdateKeepsGroupIdentityAndRefusesStaleVersionsAndForeignIds()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;

        // Relógio parado: salvar só grupos não pode depender de UpdatedAt mudar para avançar a versão.
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var ownerEmail = UniqueEmail("stage-update-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Edita");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);
        var otherCompetitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var stage = await CreateGroupStageAsync(owner, competitionId, ["A", "B"], cancellationToken);
        var otherStage = await CreateGroupStageAsync(owner, otherCompetitionId, ["X"], cancellationToken);
        var groupA = GroupId(stage, 0);

        using var updated = await owner.PutAsJsonAsync(
            StageOf(competitionId, stage),
            new
            {
                name = "Primeira fase",
                format = "Groups",
                groups = new object[] { new { name = "Grupo C" }, new { id = groupA, name = "Grupo A" } },
                tiebreakers = GoalDifferenceThenWins,
                version = Version(stage),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var current = await updated.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(["Grupo C", "Grupo A"], current.GetProperty("groups").EnumerateArray()
            .Select(group => group.GetProperty("name").GetString()));
        Assert.Equal(groupA, GroupId(current, 1));
        Assert.NotEqual(Version(stage), Version(current));

        // Só um grupo muda: a linha da fase fica igual, mas a versão precisa avançar mesmo assim.
        using var groupsOnly = await owner.PutAsJsonAsync(
            StageOf(competitionId, current),
            new
            {
                name = "Primeira fase",
                format = "Groups",
                groups = new object[]
                {
                    new { id = GroupId(current, 0), name = "Grupo C" },
                    new { id = groupA, name = "Grupo A1" },
                },
                tiebreakers = GoalDifferenceThenWins,
                version = Version(current),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, groupsOnly.StatusCode);
        var afterGroups = await groupsOnly.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.NotEqual(Version(current), Version(afterGroups));

        using var stale = await owner.PutAsJsonAsync(
            StageOf(competitionId, current), GroupsBody(current, [(groupA, "Leitura velha")]), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var foreignGroup = await owner.PutAsJsonAsync(
            StageOf(competitionId, afterGroups),
            GroupsBody(afterGroups, [(GroupId(otherStage, 0), "Intruso")]),
            cancellationToken);
        Assert.Contains("Groups", await ValidationErrorsAsync(foreignGroup, cancellationToken));

        // Fase de outro campeonato, mesmo da mesma organização, não é encontrada pela rota.
        using var foreignStage = await owner.PutAsJsonAsync(
            StageOf(competitionId, otherStage), GroupsBody(otherStage, [(null, "Y")]), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignStage.StatusCode);
        using var foreignDelete = await owner.DeleteAsync(StageOf(competitionId, otherStage), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

        using var unknownCriterion = await owner.PutAsJsonAsync(
            StageOf(competitionId, afterGroups),
            new
            {
                name = "Grupos",
                format = "Groups",
                groups = new[] { new { name = "A" } },
                tiebreakers = UnknownCriterion,
                version = Version(afterGroups),
            },
            cancellationToken);
        Assert.Contains("Tiebreakers", await ValidationErrorsAsync(unknownCriterion, cancellationToken));

        using var knockoutWithGroups = await owner.PostAsJsonAsync(
            StagesOf(competitionId),
            new { name = "Mata-mata", format = "Knockout", groups = new[] { new { name = "A" } } },
            cancellationToken);
        Assert.Contains("Groups", await ValidationErrorsAsync(knockoutWithGroups, cancellationToken));

        using var numericFormat = await owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Mata-mata", format = "2" }, cancellationToken);
        Assert.Contains("Format", await ValidationErrorsAsync(numericFormat, cancellationToken));

        using var withoutVersion = await owner.PutAsJsonAsync(
            StageOf(competitionId, afterGroups),
            KnockoutBody(afterGroups, "Sem versão") with { Version = null },
            cancellationToken);
        Assert.Contains("Version", await ValidationErrorsAsync(withoutVersion, cancellationToken));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var groupNames = await dbContext.Set<Domain.Competitions.StageGroup>()
            .Where(group => EF.Property<Guid>(group, "StageId") == Id(stage))
            .OrderBy(group => group.Sequence)
            .Select(group => group.Name)
            .ToListAsync(cancellationToken);
        Assert.Equal(["Grupo C", "Grupo A1"], groupNames);
        Assert.Equal("X", await dbContext.Set<Domain.Competitions.StageGroup>()
            .Where(group => EF.Property<Guid>(group, "StageId") == Id(otherStage))
            .Select(group => group.Name)
            .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task ReorderAndDeleteKeepAContinuousSequence()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("stage-order-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Ordena");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var first = await CreateKnockoutAsync(owner, competitionId, "Quartas", cancellationToken);
        var second = await CreateKnockoutAsync(owner, competitionId, "Semifinal", cancellationToken);
        var third = await CreateKnockoutAsync(owner, competitionId, "Final", cancellationToken);

        using var reordered = await owner.PutAsJsonAsync(
            OrderOf(competitionId), new { stageIds = new[] { Id(third), Id(first), Id(second) } }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal(["Final", "Quartas", "Semifinal"], await NamesAsync(owner, competitionId, cancellationToken));

        using var incomplete = await owner.PutAsJsonAsync(
            OrderOf(competitionId), new { stageIds = new[] { Id(first), Id(second) } }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        using var repeated = await owner.PutAsJsonAsync(
            OrderOf(competitionId), new { stageIds = new[] { Id(first), Id(first), Id(second) } }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        using var deleted = await owner.DeleteAsync(StageOf(competitionId, first), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await owner.GetFromJsonAsync<JsonElement>(StagesOf(competitionId), cancellationToken);
        Assert.Equal(
            [("Final", 1), ("Semifinal", 2)],
            remaining.EnumerateArray().Select(stage =>
                (stage.GetProperty("name").GetString(), stage.GetProperty("sequence").GetInt32())));
        using var deletedAgain = await owner.DeleteAsync(StageOf(competitionId, first), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);

        for (var index = 3; index <= 10; index++)
        {
            await CreateKnockoutAsync(owner, competitionId, $"Fase {index}", cancellationToken);
        }

        using var beyondLimit = await owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = "Fase 11", format = "Knockout" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, beyondLimit.StatusCode);
        var problem = await beyondLimit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("competition_stage_limit", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SimultaneousCreatesReceiveDistinctPositions()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("stage-race-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Corrida");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(index => owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name = $"Fase {index}", format = "Knockout" }, cancellationToken)));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            response.Dispose();
        }

        var stages = await owner.GetFromJsonAsync<JsonElement>(StagesOf(competitionId), cancellationToken);
        Assert.Equal(
            [1, 2, 3, 4, 5, 6],
            stages.EnumerateArray().Select(stage => stage.GetProperty("sequence").GetInt32()));
    }

    private static Uri StagesOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/stages", UriKind.Relative);

    private static Uri StageOf(Guid competitionId, JsonElement stage) =>
        new($"/api/v1/competitions/{competitionId}/stages/{Id(stage)}", UriKind.Relative);

    private static Uri OrderOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/stages/order", UriKind.Relative);

    private static Guid Id(JsonElement stage) => stage.GetProperty("id").GetGuid();

    private static string? Version(JsonElement stage) => stage.GetProperty("version").GetString();

    private static Guid GroupId(JsonElement stage, int index) =>
        stage.GetProperty("groups")[index].GetProperty("id").GetGuid();

    private static StageBody KnockoutBody(JsonElement stage, string name) =>
        new(name, "Knockout", [], null, Version(stage));

    private static StageBody GroupsBody(JsonElement stage, IEnumerable<(Guid? Id, string Name)> groups) =>
        new(
            "Fase de grupos",
            "Groups",
            [.. groups.Select(group => new GroupBody(group.Id, group.Name))],
            ["Wins"],
            Version(stage));

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name = "Copa com Fases", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return Id(await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static async Task<JsonElement> CreateGroupStageAsync(
        HttpClient owner,
        Guid competitionId,
        string[] groups,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            StagesOf(competitionId),
            new { name = "Fase de grupos", format = "Groups", groups = groups.Select(name => new { name }) },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<JsonElement> CreateKnockoutAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            StagesOf(competitionId), new { name, format = "Knockout" }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<string?[]> NamesAsync(
        HttpClient client,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var stages = await client.GetFromJsonAsync<JsonElement>(StagesOf(competitionId), cancellationToken);
        return [.. stages.EnumerateArray().Select(stage => stage.GetProperty("name").GetString())];
    }

    private static async Task<string[]> ValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    private sealed record StageBody(
        string Name,
        string Format,
        IReadOnlyList<GroupBody> Groups,
        IReadOnlyList<string>? Tiebreakers,
        string? Version);

    private sealed record GroupBody(Guid? Id, string Name);
}
