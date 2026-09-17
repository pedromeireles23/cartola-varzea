using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Rodadas e partidas: quem pode mexer, o que o fuso do campeonato decide e o que o
/// estado da rodada congela (Fase 7).
/// </summary>
public sealed class CompetitionRoundFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task OwnerBuildsARoundOpensTheMarketAndTheAssistantOnlyReads()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("round-owner");
        var assistantEmail = UniqueEmail("round-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Rodadas");
        await AddAssistantAsync(factory, organizationId, assistantId);

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken);

        var round = await CreateRoundAsync(owner, world.CompetitionId, "Rodada 1", cancellationToken);
        Assert.Equal("Draft", round.GetProperty("status").GetString());
        Assert.Equal("Draft", round.GetProperty("phase").GetString());

        // O horário vai como texto local; quem converte é o servidor, pelo fuso do campeonato.
        var kickoff = DateTimeOffset.UtcNow.AddDays(7);
        var kickoffLocal = kickoff.ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        using var created = await owner.PostAsJsonAsync(
            Matches(world.CompetitionId, round),
            new
            {
                stageId = world.StageId,
                homeTeamId = world.Alpha,
                awayTeamId = world.Beta,
                kickoffLocal,
                version = Version(round),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var withMatch = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var match = withMatch.GetProperty("matches")[0];
        Assert.Equal("Alpha", match.GetProperty("homeTeamName").GetString());
        Assert.Equal("Beta", match.GetProperty("awayTeamName").GetString());
        Assert.Equal("Grupo A", match.GetProperty("groupName").GetString());
        Assert.Equal("Scheduled", match.GetProperty("status").GetString());
        Assert.Equal(kickoffLocal, match.GetProperty("kickoffLocal").GetString());
        Assert.Equal(
            kickoff.ToUnixTimeSeconds() / 60,
            match.GetProperty("kickoffAt").GetDateTimeOffset().ToUnixTimeSeconds() / 60);

        // Abrir o mercado congela o fechamento na primeira partida menos a antecedência.
        using var opened = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "OpenMarket", version = Version(withMatch) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var open = await opened.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("MarketOpen", open.GetProperty("status").GetString());
        Assert.Equal("MarketOpen", open.GetProperty("phase").GetString());

        // Antecedência de 60 minutos, configurada no campeonato.
        Assert.Equal(
            match.GetProperty("kickoffAt").GetDateTimeOffset().AddHours(-1),
            open.GetProperty("marketCloseAt").GetDateTimeOffset());

        // Com o mercado aberto, a lista de jogos está congelada.
        using var lockedAdd = await owner.PostAsJsonAsync(
            Matches(world.CompetitionId, round),
            new
            {
                stageId = world.StageId,
                homeTeamId = world.Beta,
                awayTeamId = world.Alpha,
                kickoffLocal,
                version = Version(open),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, lockedAdd.StatusCode);
        Assert.Equal("competition_round_status", await CodeAsync(lockedAdd, cancellationToken));

        // Adiar continua valendo: é o que acontece quando chove no domingo.
        using var postponed = await owner.PutAsJsonAsync(
            Match(world.CompetitionId, round, match),
            new
            {
                stageId = world.StageId,
                homeTeamId = world.Alpha,
                awayTeamId = world.Beta,
                kickoffLocal,
                status = "Postponed",
                version = Version(open),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, postponed.StatusCode);
        var adiada = await postponed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Postponed", adiada.GetProperty("matches")[0].GetProperty("status").GetString());

        // Adiar não reabre o mercado: o fechamento continua onde foi congelado.
        Assert.Equal(
            open.GetProperty("marketCloseAt").GetDateTimeOffset(),
            adiada.GetProperty("marketCloseAt").GetDateTimeOffset());

        var assistantRounds = await assistant.GetFromJsonAsync<JsonElement>(
            Rounds(world.CompetitionId), cancellationToken);
        Assert.Equal(1, assistantRounds.GetArrayLength());
        using var assistantWrite = await assistant.PostAsJsonAsync(
            Rounds(world.CompetitionId), new { name = "Rodada 2" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantWrite.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var roundId = Id(round);
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionRoundMarketOpened" && entry.TargetId == roundId,
            cancellationToken));
    }

    [Fact]
    public async Task MatchesOnlyPairTeamsConfirmedInTheSameStageAndGroup()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("round-pairing-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga dos Grupos");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken, secondGroup: true);

        var round = await CreateRoundAsync(owner, world.CompetitionId, "Rodada 1", cancellationToken);
        var kickoffLocal = DateTimeOffset.UtcNow.AddDays(7)
            .ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

        // Gama está no Grupo B: numa fase de grupos ele não joga contra o Grupo A.
        var crossGroup = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Gama, kickoffLocal, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, crossGroup.Status);
        Assert.Contains("mesmo grupo", await MessageAsync(crossGroup.Response, cancellationToken));

        // Time do campeonato que não foi confirmado nesta fase também não joga.
        var outsider = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Reserva, kickoffLocal, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, outsider.Status);
        Assert.Contains("confirmado nesta fase", await MessageAsync(outsider.Response, cancellationToken));

        var sameTeam = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Alpha, kickoffLocal, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, sameTeam.Status);

        var invalidTime = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Beta, "20/09/2026 15:30", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTime.Status);
        Assert.Contains("data e a hora", await MessageAsync(invalidTime.Response, cancellationToken));

        var valid = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Beta, kickoffLocal, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.Status);
        valid.Response.Dispose();

        // Mesmo time pode jogar duas vezes na mesma rodada: domingo concentrado (01 §7).
        var current = await owner.GetFromJsonAsync<JsonElement>(Rounds(world.CompetitionId), cancellationToken);
        var again = await PostMatchAsync(
            owner, world, current[0], world.Beta, world.Alpha, kickoffLocal, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        var twice = await again.Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(2, twice.GetProperty("matches").GetArrayLength());
        again.Response.Dispose();

        // A fase agora tem partidas: trocar o formato e removê-la passam a ser recusados.
        var stages = await owner.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/competitions/{world.CompetitionId}/stages", UriKind.Relative),
            cancellationToken);
        using var formatChange = await owner.PutAsJsonAsync(
            new Uri($"/api/v1/competitions/{world.CompetitionId}/stages/{world.StageId}", UriKind.Relative),
            new
            {
                name = "Mata-mata",
                format = "Knockout",
                groups = Array.Empty<object>(),
                tiebreakers = Array.Empty<string>(),
                version = stages[0].GetProperty("version").GetString(),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, formatChange.StatusCode);

        using var stageDelete = await owner.DeleteAsync(
            new Uri($"/api/v1/competitions/{world.CompetitionId}/stages/{world.StageId}", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stageDelete.StatusCode);
        Assert.Equal("competition_stage_dependencies", await CodeAsync(stageDelete, cancellationToken));
    }

    [Fact]
    public async Task RoundProtectsItsVersionAndRefusesImpossibleTransitions()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("round-guard-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Protegida");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken);
        var round = await CreateRoundAsync(owner, world.CompetitionId, "Rodada 1", cancellationToken);

        // Sem partida marcada não há o que abrir.
        using var empty = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "OpenMarket", version = Version(round) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("ao menos uma partida", await MessageAsync(empty, cancellationToken));

        // Partida que começa antes da antecedência faria o mercado nascer fechado.
        var soon = DateTimeOffset.UtcNow.AddMinutes(20)
            .ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        var soonMatch = await PostMatchAsync(
            owner, world, round, world.Alpha, world.Beta, soon, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, soonMatch.Status);
        var withSoon = await soonMatch.Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        soonMatch.Response.Dispose();

        using var bornClosed = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "OpenMarket", version = Version(withSoon) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bornClosed.StatusCode);
        Assert.Contains("já teria fechado", await MessageAsync(bornClosed, cancellationToken));

        // Versão velha não decide nada.
        using var stale = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "Cancel", version = Version(round) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var cancelled = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "Cancel", version = Version(withSoon) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var dead = await cancelled.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Cancelled", dead.GetProperty("phase").GetString());

        // Uma rodada cancelada não volta atrás nem é removida.
        using var revive = await owner.PutAsJsonAsync(
            Status(world.CompetitionId, round),
            new { transition = "ReopenForEditing", version = Version(dead) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, revive.StatusCode);

        using var removal = await owner.DeleteAsync(Round(world.CompetitionId, round), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, removal.StatusCode);
        Assert.Equal("competition_round_status", await CodeAsync(removal, cancellationToken));
    }

    private static Uri Rounds(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/rounds", UriKind.Relative);

    private static Uri Round(Guid competitionId, JsonElement round) =>
        new($"/api/v1/competitions/{competitionId}/rounds/{Id(round)}", UriKind.Relative);

    private static Uri Status(Guid competitionId, JsonElement round) =>
        new($"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/status", UriKind.Relative);

    private static Uri Matches(Guid competitionId, JsonElement round) =>
        new($"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/matches", UriKind.Relative);

    private static Uri Match(Guid competitionId, JsonElement round, JsonElement match) =>
        new($"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/matches/{Id(match)}", UriKind.Relative);

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string? Version(JsonElement item) => item.GetProperty("version").GetString();

    private static async Task<string?> CodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return problem.GetProperty("code").GetString();
    }

    /// <summary>Junta as mensagens de validação para procurar a frase que interessa.</summary>
    private static async Task<string> MessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return problem.TryGetProperty("errors", out var errors)
            ? string.Join(
                " | ",
                errors.EnumerateObject().SelectMany(
                    error => error.Value.EnumerateArray().Select(item => item.GetString())))
            : problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    private static async Task<(HttpStatusCode Status, HttpResponseMessage Response)> PostMatchAsync(
        HttpClient owner,
        World world,
        JsonElement round,
        Guid home,
        Guid away,
        string kickoffLocal,
        CancellationToken cancellationToken)
    {
        var response = await owner.PostAsJsonAsync(
            Matches(world.CompetitionId, round),
            new
            {
                stageId = world.StageId,
                homeTeamId = home,
                awayTeamId = away,
                kickoffLocal,
                version = Version(round),
            },
            cancellationToken);
        return (response.StatusCode, response);
    }

    private static async Task<JsonElement> CreateRoundAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(Rounds(competitionId), new { name }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>Campeonato com fase de grupos, times confirmados e antecedência de uma hora.</summary>
    private static async Task<World> BuildAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken,
        bool secondGroup = false)
    {
        using var createdCompetition = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new
            {
                name = "Copa das Rodadas",
                season = "2026",
                modality = "Fut7",
                marketCloseLeadTimeMinutes = 60,
            },
            cancellationToken);
        createdCompetition.EnsureSuccessStatusCode();
        var competitionId = Id(
            await createdCompetition.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));

        var alpha = await CreateTeamAsync(owner, competitionId, "Alpha", cancellationToken);
        var beta = await CreateTeamAsync(owner, competitionId, "Beta", cancellationToken);
        var gama = await CreateTeamAsync(owner, competitionId, "Gama", cancellationToken);
        var reserva = await CreateTeamAsync(owner, competitionId, "Reserva", cancellationToken);

        object[] groups = secondGroup
            ? [new { name = "Grupo A" }, new { name = "Grupo B" }]
            : [new { name = "Grupo A" }];
        using var createdStage = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/competitions/{competitionId}/stages", UriKind.Relative),
            new { name = "Fase de grupos", format = "Groups", groups },
            cancellationToken);
        createdStage.EnsureSuccessStatusCode();
        var stage = await createdStage.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var groupA = stage.GetProperty("groups")[0].GetProperty("id").GetGuid();
        var groupB = secondGroup ? stage.GetProperty("groups")[1].GetProperty("id").GetGuid() : groupA;

        using var participants = await owner.PutAsJsonAsync(
            new Uri(
                $"/api/v1/competitions/{competitionId}/stages/{Id(stage)}/participants", UriKind.Relative),
            new
            {
                participants = new[]
                {
                    new { realTeamId = alpha, stageGroupId = groupA },
                    new { realTeamId = beta, stageGroupId = groupA },
                    new { realTeamId = gama, stageGroupId = groupB },
                },
                version = Version(stage),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        return new World(competitionId, Id(stage), alpha, beta, gama, reserva);
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

    /// <summary>Cenário montado uma vez por teste; `Reserva` existe mas não entra na fase.</summary>
    private sealed record World(
        Guid CompetitionId,
        Guid StageId,
        Guid Alpha,
        Guid Beta,
        Guid Gama,
        Guid Reserva);
}
