using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Checklist de prontidão e publicação: quem lê, quem publica, o que impede e o que a
/// publicação trava depois (Fase 5).
/// </summary>
public sealed class CompetitionPublicationFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    /// <summary>Elenco mínimo do Fut7: titulares da formação e um reserva por posição.</summary>
    private static readonly (string Position, int Count)[] MinimumSquad =
    [
        ("Goalkeeper", 2),
        ("Defender", 3),
        ("Midfielder", 3),
        ("Forward", 3),
    ];

    [Fact]
    public async Task EmptyDraftIsBlockedAndAFilledCompetitionPublishes()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("publication-owner");
        var assistantEmail = UniqueEmail("publication-assistant");
        var outsiderEmail = UniqueEmail("publication-outsider");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var outsiderId = await CreateUserAsync(factory, outsiderEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Publicação");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, outsiderId, "Liga de Fora");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var outsider = await CreateAuthenticatedClientAsync(factory, outsiderEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        // Rascunho vazio: o checklist lista impedimentos e a publicação é recusada.
        var empty = await owner.GetFromJsonAsync<JsonElement>(
            ReadinessOf(competitionId), cancellationToken);
        Assert.False(empty.GetProperty("canPublish").GetBoolean());
        Assert.Equal("Draft", empty.GetProperty("status").GetString());
        Assert.Contains("no_stages", Codes(empty));
        Assert.Contains("not_enough_teams", Codes(empty));
        Assert.Contains("not_enough_athletes", Codes(empty));

        using var tooEarly = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = true, version = Version(empty) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        var problem = await tooEarly.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("competition_not_ready", problem.GetProperty("code").GetString());

        // O problema carrega o checklist: a tela explica o que falta sem uma segunda ida.
        Assert.Contains("no_stages", Codes(problem.GetProperty("readiness")));

        var alpha = await CreateTeamAsync(owner, competitionId, "Alpha", cancellationToken);
        var beta = await CreateTeamAsync(owner, competitionId, "Beta", cancellationToken);
        await CreateSquadAsync(owner, competitionId, alpha, beta, cancellationToken);
        var stage = await CreateGroupStageAsync(owner, competitionId, cancellationToken);
        using var participants = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, stage),
            new
            {
                participants = new[]
                {
                    new { realTeamId = alpha, stageGroupId = GroupId(stage) },
                    new { realTeamId = beta, stageGroupId = GroupId(stage) },
                },
                version = Version(stage),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        // Auxiliar acompanha o checklist, mas não decide a publicação.
        var ready = await assistant.GetFromJsonAsync<JsonElement>(
            ReadinessOf(competitionId), cancellationToken);
        Assert.True(ready.GetProperty("canPublish").GetBoolean());

        // Elenco mínimo do campeonato ainda é pouco por time: alerta, nunca impedimento.
        Assert.Equal(["thin_real_team_roster", "thin_real_team_roster"], Codes(ready));
        using var assistantWrite = await assistant.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = true, version = Version(ready) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantWrite.StatusCode);
        using var outsiderRead = await outsider.GetAsync(ReadinessOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRead.StatusCode);

        using var published = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = true, version = Version(ready) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var current = await published.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Published", current.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, current.GetProperty("publishedAt").ValueKind);
        Assert.NotEqual(Version(ready), Version(current));

        // Publicar de novo o mesmo estado confirma sem gravar nada.
        using var again = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = true, version = Version(current) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(Version(current), Version(
            await again.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionPublished" && entry.TargetId == competitionId,
            cancellationToken));
    }

    [Fact]
    public async Task PublicationLocksTheModalityAndProtectsTheVersion()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("publication-lock-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Travada");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var alpha = await CreateTeamAsync(owner, competitionId, "Alpha", cancellationToken);
        var beta = await CreateTeamAsync(owner, competitionId, "Beta", cancellationToken);
        await CreateSquadAsync(owner, competitionId, alpha, beta, cancellationToken);
        var stage = await CreateGroupStageAsync(owner, competitionId, cancellationToken);
        using var participants = await owner.PutAsJsonAsync(
            ParticipantsOf(competitionId, stage),
            new
            {
                participants = new[] { new { realTeamId = alpha, stageGroupId = GroupId(stage) } },
                version = Version(stage),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        var readiness = await owner.GetFromJsonAsync<JsonElement>(
            ReadinessOf(competitionId), cancellationToken);
        using var published = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = true, version = Version(readiness) },
            cancellationToken);
        published.EnsureSuccessStatusCode();

        // Uma segunda decisão sobre a leitura antiga não pode valer.
        using var stale = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = false, version = Version(readiness) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var settings = await owner.GetFromJsonAsync<JsonElement>(
            SettingsOf(competitionId), cancellationToken);
        Assert.Equal("Published", settings.GetProperty("status").GetString());
        Assert.False(settings.GetProperty("canChangeModality").GetBoolean());
        using var modalityChange = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            SettingsBody(settings, modality: "Futsal"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, modalityChange.StatusCode);
        Assert.Equal(
            "competition_modality_locked",
            (await modalityChange.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
                .GetProperty("code").GetString());

        var current = await owner.GetFromJsonAsync<JsonElement>(
            ReadinessOf(competitionId), cancellationToken);
        using var unpublished = await owner.PutAsJsonAsync(
            PublicationOf(competitionId),
            new { published = false, version = Version(current) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        var draft = await unpublished.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Draft", draft.GetProperty("status").GetString());

        // Voltar para rascunho preserva o instante da publicação e não destrava a modalidade.
        Assert.NotEqual(JsonValueKind.Null, draft.GetProperty("publishedAt").ValueKind);
        var reopened = await owner.GetFromJsonAsync<JsonElement>(SettingsOf(competitionId), cancellationToken);
        Assert.False(reopened.GetProperty("canChangeModality").GetBoolean());
        using var modalityAfterDraft = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            SettingsBody(reopened, modality: "Futsal"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, modalityAfterDraft.StatusCode);

        // O resto da configuração continua editável no rascunho.
        using var seasonChange = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            SettingsBody(reopened, modality: "Fut7", season: "2027"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, seasonChange.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionUnpublished" && entry.TargetId == competitionId,
            cancellationToken));
    }

    private static Uri ReadinessOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/readiness", UriKind.Relative);

    private static Uri PublicationOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/publication", UriKind.Relative);

    private static Uri SettingsOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/settings", UriKind.Relative);

    private static Uri StagesOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/stages", UriKind.Relative);

    private static Uri ParticipantsOf(Guid competitionId, JsonElement stage) =>
        new($"/api/v1/competitions/{competitionId}/stages/{Id(stage)}/participants", UriKind.Relative);

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string? Version(JsonElement item) => item.GetProperty("version").GetString();

    private static Guid GroupId(JsonElement stage) =>
        stage.GetProperty("groups")[0].GetProperty("id").GetGuid();

    private static string[] Codes(JsonElement readiness) =>
    [
        .. readiness.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString() ?? string.Empty)
    ];

    private static object SettingsBody(JsonElement settings, string modality, string? season = null) => new
    {
        name = settings.GetProperty("name").GetString(),
        season = season ?? settings.GetProperty("season").GetString(),
        modality,
        timeZoneId = settings.GetProperty("timeZoneId").GetString(),
        marketCloseLeadTimeMinutes = settings.GetProperty("marketCloseLeadTimeMinutes").GetInt32(),
        resultsSlaBusinessDays = settings.GetProperty("resultsSlaBusinessDays").GetInt32(),
        correctionWindowBusinessDays = settings.GetProperty("correctionWindowBusinessDays").GetInt32(),
        version = Version(settings),
    };

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name = "Copa Publicável", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return Id(await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static async Task<Guid> CreateTeamAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/competitions/{competitionId}/teams", UriKind.Relative),
            new { name },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return Id(await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    /// <summary>
    /// Inscreve o elenco mínimo alternando os dois times, para que nenhuma posição fique
    /// abaixo do necessário e a publicação dependa só do que o teste quer provar.
    /// </summary>
    private static async Task CreateSquadAsync(
        HttpClient owner,
        Guid competitionId,
        Guid alpha,
        Guid beta,
        CancellationToken cancellationToken)
    {
        var index = 0;
        foreach (var (position, count) in MinimumSquad)
        {
            for (var number = 1; number <= count; number++)
            {
                using var created = await owner.PostAsJsonAsync(
                    new Uri($"/api/v1/competitions/{competitionId}/athletes", UriKind.Relative),
                    new
                    {
                        sportingName = $"{position} {number}",
                        position,
                        realTeamId = index++ % 2 == 0 ? alpha : beta,
                        priceTier = number == 1 ? "Star" : "Regular",
                    },
                    cancellationToken);
                created.EnsureSuccessStatusCode();
            }
        }
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
                name = "Fase única",
                format = "Groups",
                groups = new[] { new { name = "Grupo A" } },
            },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }
}
