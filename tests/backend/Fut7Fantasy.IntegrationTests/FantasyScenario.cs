using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Fut7Fantasy.IntegrationTests.ImportRequests;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// O mundo dos testes do jogo: a massa fictícia de `infra/dados-demo` publicada, com
/// rodadas marcadas, e participações montadas pelas rotas reais do fantasy. Usado pelo
/// mercado, pela escalação e pela apuração.
/// </summary>
internal static class FantasyScenario
{
    internal static readonly string[] SquadPositions =
    [
        "Goalkeeper", "Goalkeeper", "Defender", "Defender", "Defender", "Midfielder", "Midfielder",
        "Midfielder", "Forward", "Forward", "Forward",
    ];

    internal static Uri Uri(string slug, string path) =>
        new($"/api/v1/fantasy/{slug}/{path}", UriKind.Relative);

    internal static Guid Id(JsonElement item) =>
        item.TryGetProperty("assetId", out var assetId) ? assetId.GetGuid() : item.GetProperty("id").GetGuid();

    internal static async Task<JsonElement> OverviewAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<JsonElement>(Uri(slug, string.Empty), cancellationToken);

    internal static async Task<List<JsonElement>> MarketAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        [
            .. (await client.GetFromJsonAsync<JsonElement>(Uri(slug, "market"), cancellationToken))
                .GetProperty("items").EnumerateArray(),
        ];

    internal static Task<HttpResponseMessage> BuyAsync(
        HttpClient client,
        string slug,
        JsonElement item,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            Uri(slug, $"squad/{(item.GetProperty("kind").GetString() == "Coach" ? "tecnico" : "atleta")}/{Id(item)}"),
            null,
            cancellationToken);

    /// <summary>Adere e compra o elenco completo mais barato, com capitão; devolve o capitão.</summary>
    internal static async Task<Guid> JoinWithCompleteSquadAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken)
    {
        using var joined = await client.PostAsync(Uri(slug, "entry"), null, cancellationToken);
        joined.EnsureSuccessStatusCode();

        foreach (var position in SquadPositions)
        {
            var choice = (await MarketAsync(client, slug, cancellationToken))
                .Where(item => item.GetProperty("position").GetString() == position
                    && item.GetProperty("blockCode").ValueKind == JsonValueKind.Null)
                .MinBy(item => item.GetProperty("price").GetDecimal());
            using var bought = await BuyAsync(client, slug, choice, cancellationToken);
            bought.EnsureSuccessStatusCode();
        }

        var coach = (await MarketAsync(client, slug, cancellationToken))
            .Where(item => item.GetProperty("kind").GetString() == "Coach")
            .MinBy(item => item.GetProperty("price").GetDecimal());
        using var coachBought = await BuyAsync(client, slug, coach, cancellationToken);
        coachBought.EnsureSuccessStatusCode();

        var slots = (await OverviewAsync(client, slug, cancellationToken))
            .GetProperty("entry").GetProperty("slots").EnumerateArray().ToList();
        var captainSlot = slots.First(slot => slot.GetProperty("role").GetString() == "Starter");
        using var captain = await CaptainAsync(client, slug, captainSlot, cancellationToken);
        captain.EnsureSuccessStatusCode();
        return Id(captainSlot);
    }

    internal static Task<HttpResponseMessage> CaptainAsync(
        HttpClient client,
        string slug,
        JsonElement slot,
        CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(Uri(slug, "lineup/captain"), new { athleteId = Id(slot) }, cancellationToken);

    /// <summary>Massa de demonstração publicada, com a Rodada 1 marcada para daqui a dois dias.</summary>
    internal static async Task<World> BuildAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var ownerEmail = UniqueEmail("fantasy-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga do Fantasy");
        var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        using var created = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = $"Copa Fantasy {Guid.NewGuid():N}"[..20], season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var competitionId = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("id").GetGuid();
        foreach (var (kind, file) in new[]
        {
            ("times", "times-v1.csv"), ("atletas", "atletas-v1.csv"), ("tecnicos", "tecnicos-v1.csv"),
        })
        {
            var imported = await PostAsync(owner, competitionId, kind, DemoFile(file), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, imported.Status);
        }

        using var stageResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages",
            new { name = "Pontos corridos", format = "Groups", groups = new[] { new { name = "Grupo único" } } },
            cancellationToken);
        stageResponse.EnsureSuccessStatusCode();
        var stage = await stageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var groupId = stage.GetProperty("groups")[0].GetProperty("id").GetGuid();
        var teams = (await owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{competitionId}/teams", cancellationToken))
            .EnumerateArray()
            .Select(team => team.GetProperty("id").GetGuid())
            .ToList();
        using var participants = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages/{stage.GetProperty("id").GetGuid()}/participants",
            new
            {
                participants = teams.Select(team => new { realTeamId = team, stageGroupId = groupId }),
                version = stage.GetProperty("version").GetString(),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        var readiness = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{competitionId}/readiness", cancellationToken);
        using var published = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/publication",
            new { published = true, version = readiness.GetProperty("version").GetString() },
            cancellationToken);
        published.EnsureSuccessStatusCode();
        var slug = (await published.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("slug").GetString()!;

        var world = new World(
            owner, competitionId, slug, stage.GetProperty("id").GetGuid(), teams, Guid.Empty, organizationId);
        var round = await CreateRoundAsync(owner, world, "Rodada 1", daysAhead: 2, cancellationToken);
        return world with { RoundId = round.GetProperty("id").GetGuid() };
    }

    internal static async Task<JsonElement> CreateRoundAsync(
        HttpClient owner,
        World world,
        string name,
        int daysAhead,
        CancellationToken cancellationToken)
    {
        using var roundResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds", new { name }, cancellationToken);
        roundResponse.EnsureSuccessStatusCode();
        var round = await roundResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var match = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{round.GetProperty("id").GetGuid()}/matches",
            new
            {
                stageId = world.StageId,
                homeTeamId = world.Teams[0],
                awayTeamId = world.Teams[1],
                kickoffLocal = ApiFactory.FixedNow.AddDays(daysAhead).ToOffset(TimeSpan.FromHours(-3))
                    .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                version = round.GetProperty("version").GetString(),
            },
            cancellationToken);
        match.EnsureSuccessStatusCode();
        return await match.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    internal static async Task OpenMarketAsync(HttpClient owner, World world, CancellationToken cancellationToken)
    {
        var rounds = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds", cancellationToken);
        var round = rounds.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == world.RoundId);
        using var opened = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{world.RoundId}/status",
            new { transition = "OpenMarket", version = round.GetProperty("version").GetString() },
            cancellationToken);
        opened.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Conferência, publicação e súmula da rodada, compartilhadas entre a apuração e o
    /// ranking: os dois precisam de um campeonato com resultado para ter o que medir.
    /// </summary>
    internal static Task<HttpResponseMessage> ReopenAsync(
        HttpClient client,
        World world,
        Guid roundId,
        string version,
        string? reason,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/reopen",
            new { version, reason },
            cancellationToken);

    /// <summary>Manda para revisão se preciso e publica; a rodada reaberta já está lá.</summary>
    internal static async Task PublishRoundAsync(World world, Guid roundId, CancellationToken cancellationToken)
    {
        var round = await RoundAsync(world, roundId, cancellationToken);
        var version = round.GetProperty("status").GetString() == "UnderReview"
            ? round.GetProperty("version").GetString()!
            : await SendToReviewAsync(world, roundId, cancellationToken);
        using var published = await PublishAsync(world.Owner, world, roundId, version, cancellationToken);
        published.EnsureSuccessStatusCode();
    }

    internal static Task<HttpResponseMessage> PublishAsync(
        HttpClient client,
        World world,
        Guid roundId,
        string version,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/publish",
            new { version },
            cancellationToken);

    internal static async Task<JsonElement> ReviewAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken) =>
        await world.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/review", cancellationToken);

    internal static async Task<JsonElement> RoundAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken) =>
        (await world.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds", cancellationToken))
        .EnumerateArray()
        .Single(item => item.GetProperty("id").GetGuid() == roundId);

    internal static async Task<string> RoundVersionAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken) =>
        (await RoundAsync(world, roundId, cancellationToken)).GetProperty("version").GetString()!;

    /// <summary>Manda a rodada para revisão e devolve a versão nova, que a publicação exige.</summary>
    internal static async Task<string> SendToReviewAsync(World world, Guid roundId, CancellationToken cancellationToken)
    {
        using var response = await world.Owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/status",
            new { transition = "SendToReview", version = await RoundVersionAsync(world, roundId, cancellationToken) },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("version").GetString()!;
    }

    /// <summary>
    /// Súmula do único jogo da rodada: mandante 1 × 0, gol do primeiro atacante do
    /// mandante; cada time com um goleiro em campo e o outro fora; os demais jogaram.
    /// </summary>
    internal static async Task<SheetFacts> FillSheetAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken,
        int goals = 1)
    {
        var matchId = (await RoundAsync(world, roundId, cancellationToken))
            .GetProperty("matches")[0].GetProperty("id").GetGuid();
        var uri = $"/api/v1/competitions/{world.CompetitionId}/matches/{matchId}/sheet";
        var current = await world.Owner.GetFromJsonAsync<JsonElement>(uri, cancellationToken);
        var home = current.GetProperty("homeTeamId").GetGuid();
        var athletes = current.GetProperty("athletes").EnumerateArray().ToList();

        Guid First(Guid team, string position, int skip = 0) => athletes
            .Where(item => item.GetProperty("realTeamId").GetGuid() == team
                && item.GetProperty("position").GetString() == position)
            .Skip(skip)
            .First()
            .GetProperty("athleteId").GetGuid();

        var away = current.GetProperty("awayTeamId").GetGuid();
        var facts = new SheetFacts(
            matchId,
            First(home, "Forward"),
            First(home, "Goalkeeper"),
            First(home, "Goalkeeper", skip: 1),
            First(away, "Goalkeeper"),
            null);
        var benched = new[] { facts.HomeReserveGoalkeeper, First(away, "Goalkeeper", skip: 1) };
        var goalkeepers = new[] { facts.HomeGoalkeeper, facts.AwayGoalkeeper };

        var appearances = athletes.Select(item =>
        {
            var id = item.GetProperty("athleteId").GetGuid();
            return new
            {
                athleteId = id,
                didPlay = !benched.Contains(id),
                playedAsGoalkeeper = goalkeepers.Contains(id),
                goalsConceded = id == facts.AwayGoalkeeper ? goals : 0,
                goals = id == facts.Scorer ? goals : 0,
                assists = 0,
                goalkeeperSaves = 0,
                penaltySaves = 0,
                yellowCards = 0,
                redCards = 0,
                redCardReason = (string?)null,
                ownGoals = 0,
                penaltyMisses = 0,
            };
        }).ToArray();

        using var saved = await world.Owner.PutAsJsonAsync(
            uri,
            new
            {
                homeScore = goals,
                awayScore = 0,
                appearances,
                version = current.GetProperty("version").GetString(),
            },
            cancellationToken);
        saved.EnsureSuccessStatusCode();
        var version = (await saved.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("version").GetString();
        return facts with { Version = version };
    }

    internal sealed record SheetFacts(
        Guid MatchId,
        Guid Scorer,
        Guid HomeGoalkeeper,
        Guid HomeReserveGoalkeeper,
        Guid AwayGoalkeeper,
        string? Version);

    internal sealed record World(
        HttpClient Owner,
        Guid CompetitionId,
        string Slug,
        Guid StageId,
        List<Guid> Teams,
        Guid RoundId,
        Guid OrganizationId);
}
