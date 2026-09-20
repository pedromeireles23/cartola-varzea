using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.FantasyScenario;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Publicação da rodada pela API real (Fase 10): a apuração gravada, idempotente, só do
/// proprietário, em ordem, com os preços novos valendo no mercado. A súmula é sempre a
/// mesma — mandante 1 × 0, gol de um atacante do mandante —, para que os números sejam
/// conferidos à mão.
/// </summary>
public sealed class RoundPublicationFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task OwnerPublishesTheRoundOnceAndTheMarketFollowsTheNewPrices()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var playerEmail = UniqueEmail("publication-player");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        await JoinWithCompleteSquadAsync(player, world.Slug, cancellationToken);

        var assistantEmail = UniqueEmail("publication-assistant");
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        await AddAssistantAsync(factory, world.OrganizationId, assistantId);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));
        var sheet = await FillSheetAsync(world, world.RoundId, cancellationToken);
        var beforeReview = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        var version = await SendToReviewAsync(world, world.RoundId, cancellationToken);

        // Auxiliar confere, mas não publica, nem por chamada direta.
        using (var byAssistant = await PublishAsync(assistant, world, world.RoundId, version, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, byAssistant.StatusCode);
        }

        using (var stale = await PublishAsync(world.Owner, world, world.RoundId, beforeReview, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        using var published = await PublishAsync(world.Owner, world, world.RoundId, version, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var review = await published.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Published", review.GetProperty("phase").GetString());
        var publication = review.GetProperty("publication");
        Assert.Equal(1, publication.GetProperty("revision").GetInt32());
        Assert.Equal(1, publication.GetProperty("scoringRuleSetVersion").GetInt32());
        Assert.Equal(1, publication.GetProperty("entries").GetInt32());
        Assert.False(publication.GetProperty("consolidated").GetBoolean());

        // O mesmo pedido de novo não apura outra vez.
        using (var again = await PublishAsync(world.Owner, world, world.RoundId, version, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var calculation = await dbContext.RoundCalculations
            .AsNoTracking()
            .Include(item => item.Athletes)
            .Include(item => item.Coaches)
            .Include(item => item.Entries)
            .Include(item => item.EntrySlots)
            .Include(item => item.Prices)
            .SingleAsync(item => item.RoundId == world.RoundId, cancellationToken);

        // Atletas: atacante que marcou 5, goleiro do mandante sem sofrer gol 5, goleiro do
        // visitante -1; o segundo goleiro não entrou.
        decimal PointsOf(Guid athlete) => calculation.Athletes.Single(item => item.AthleteId == athlete).Points;
        Assert.Equal(5m, PointsOf(sheet.Scorer));
        Assert.Equal(5m, PointsOf(sheet.HomeGoalkeeper));
        Assert.Equal(-1m, PointsOf(sheet.AwayGoalkeeper));
        Assert.False(calculation.Athletes.Single(item => item.AthleteId == sheet.HomeReserveGoalkeeper).Played);

        // Técnicos: mandante 20 ÷ 8 = 2,5; visitante -1 ÷ 8 = -0,125, que vira -0,13.
        Assert.Equal(2.5m, calculation.Coaches.Single(item => item.RealTeamId == world.Teams[0]).Points);
        Assert.Equal(-0.13m, calculation.Coaches.Single(item => item.RealTeamId == world.Teams[1]).Points);
        Assert.False(calculation.Coaches.Single(item => item.RealTeamId == world.Teams[2]).Played);

        // O total da participação é o extrato vaga a vaga mais o bônus do capitão.
        var entry = Assert.Single(calculation.Entries);
        Assert.Equal(
            calculation.EntrySlots.Where(slot => slot.Counts).Sum(slot => slot.Points) + entry.CaptainBonus,
            entry.Total);

        // Goleiros: média (5 - 1) ÷ 2 = 2; o do mandante sobe 1, o do visitante desce 1.
        var homeGoalkeeperPrice = calculation.Prices.Single(item => item.AssetId == sheet.HomeGoalkeeper);
        Assert.Equal(2m, homeGoalkeeperPrice.Average);
        Assert.Equal(1m, homeGoalkeeperPrice.Variation);
        Assert.Equal(-1m, calculation.Prices.Single(item => item.AssetId == sheet.AwayGoalkeeper).Variation);
        var assets = await dbContext.Athletes.CountAsync(
                item => item.CompetitionId == world.CompetitionId, cancellationToken)
            + await dbContext.Coaches.CountAsync(item => item.CompetitionId == world.CompetitionId, cancellationToken);
        Assert.Equal(assets, calculation.Prices.Count);

        // O mercado passa a vender pelo preço novo.
        var market = await MarketAsync(player, world.Slug, cancellationToken);
        Assert.Equal(
            homeGoalkeeperPrice.NewPrice,
            market.Single(item => Id(item) == sheet.HomeGoalkeeper).GetProperty("price").GetDecimal());

        Assert.True(await dbContext.AdministrativeAuditEntries.AnyAsync(
            item => item.Action == "CompetitionRoundPublished" && item.TargetId == world.RoundId,
            cancellationToken));

        // A súmula usada na apuração não muda mais por fora: corrigir exige reabrir a rodada.
        using (var edit = await world.Owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/matches/{sheet.MatchId}/sheet",
            new { homeScore = 0, awayScore = 0, appearances = Array.Empty<object>(), version = sheet.Version },
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        }

        // A rodada consolida sozinha, pelo relógio, no fim da janela de correção.
        clock.Advance(TimeSpan.FromDays(10));
        var consolidated = await ReviewAsync(world, world.RoundId, cancellationToken);
        Assert.Equal("Consolidated", consolidated.GetProperty("phase").GetString());
        Assert.True(consolidated.GetProperty("publication").GetProperty("consolidated").GetBoolean());
    }

    [Fact]
    public async Task RoundsArePublishedInOrderAndEachStartsFromThePricesOfThePreviousOne()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);
        var second = (await CreateRoundAsync(world.Owner, world, "Rodada 2", daysAhead: 5, cancellationToken))
            .GetProperty("id").GetGuid();

        clock.Advance(TimeSpan.FromDays(3));
        await OpenMarketAsync(world.Owner, world with { RoundId = second }, cancellationToken);
        clock.Advance(TimeSpan.FromDays(3));

        await FillSheetAsync(world, world.RoundId, cancellationToken);
        await FillSheetAsync(world, second, cancellationToken);
        var firstVersion = await SendToReviewAsync(world, world.RoundId, cancellationToken);
        var secondVersion = await SendToReviewAsync(world, second, cancellationToken);

        using (var outOfOrder = await PublishAsync(world.Owner, world, second, secondVersion, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, outOfOrder.StatusCode);
            Assert.Contains("Publique antes a Rodada 1", await outOfOrder.Content.ReadAsStringAsync(cancellationToken));
        }

        using (var first = await PublishAsync(world.Owner, world, world.RoundId, firstVersion, cancellationToken))
        {
            first.EnsureSuccessStatusCode();
        }

        using (var then = await PublishAsync(world.Owner, world, second, secondVersion, cancellationToken))
        {
            then.EnsureSuccessStatusCode();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var calculations = await dbContext.RoundCalculations
            .AsNoTracking()
            .Include(item => item.Prices)
            .Where(item => item.CompetitionId == world.CompetitionId)
            .ToListAsync(cancellationToken);
        var afterFirst = calculations.Single(item => item.RoundId == world.RoundId).Prices
            .ToDictionary(item => (item.Kind, item.AssetId), item => item.NewPrice);
        var secondPrices = calculations.Single(item => item.RoundId == second).Prices;

        // A Rodada 2 parte do preço deixado pela Rodada 1, ativo por ativo.
        Assert.All(secondPrices, item => Assert.Equal(afterFirst[(item.Kind, item.AssetId)], item.PreviousPrice));
        Assert.Contains(secondPrices, item => item.PreviousPrice != item.NewPrice);
        Assert.Contains(
            calculations.Single(item => item.RoundId == world.RoundId).Prices,
            item => item.Kind == AssetKind.Athlete && item.PreviousPrice != item.NewPrice);
    }

    [Fact]
    public async Task OnlyARoundUnderReviewCanBePublished()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var version = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using var refused = await PublishAsync(world.Owner, world, world.RoundId, version, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("competition_round_status", body.GetProperty("code").GetString());
    }

    private static Task<HttpResponseMessage> PublishAsync(
        HttpClient client,
        World world,
        Guid roundId,
        string version,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/publish",
            new { version },
            cancellationToken);

    private static async Task<JsonElement> ReviewAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken) =>
        await world.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{roundId}/review", cancellationToken);

    private static async Task<JsonElement> RoundAsync(World world, Guid roundId, CancellationToken cancellationToken) =>
        (await world.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds", cancellationToken))
        .EnumerateArray()
        .Single(item => item.GetProperty("id").GetGuid() == roundId);

    private static async Task<string> RoundVersionAsync(
        World world,
        Guid roundId,
        CancellationToken cancellationToken) =>
        (await RoundAsync(world, roundId, cancellationToken)).GetProperty("version").GetString()!;

    /// <summary>Manda a rodada para revisão e devolve a versão nova, que a publicação exige.</summary>
    private static async Task<string> SendToReviewAsync(World world, Guid roundId, CancellationToken cancellationToken)
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
    private static async Task<SheetFacts> FillSheetAsync(World world, Guid roundId, CancellationToken cancellationToken)
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
                goalsConceded = id == facts.AwayGoalkeeper ? 1 : 0,
                goals = id == facts.Scorer ? 1 : 0,
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
            new { homeScore = 1, awayScore = 0, appearances, version = (string?)null },
            cancellationToken);
        saved.EnsureSuccessStatusCode();
        var version = (await saved.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("version").GetString();
        return facts with { Version = version };
    }

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

    private sealed record SheetFacts(
        Guid MatchId,
        Guid Scorer,
        Guid HomeGoalkeeper,
        Guid HomeReserveGoalkeeper,
        Guid AwayGoalkeeper,
        string? Version);
}
