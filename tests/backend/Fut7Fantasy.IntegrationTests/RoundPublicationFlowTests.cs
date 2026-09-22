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

        // Quem jogou lê a própria pontuação, com o detalhamento e a variação de preço.
        var rounds = await player.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds", cancellationToken);
        var listed = Assert.Single(rounds.EnumerateArray());
        Assert.Equal(world.RoundId, listed.GetProperty("roundId").GetGuid());
        Assert.Equal(entry.Total, listed.GetProperty("total").GetDecimal());
        Assert.True(listed.GetProperty("provisional").GetBoolean());

        var detail = await player.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{world.RoundId}", cancellationToken);
        Assert.True(detail.GetProperty("played").GetBoolean());
        Assert.Equal(entry.Total, detail.GetProperty("total").GetDecimal());
        var detailSlots = detail.GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(12, detailSlots.Count);
        Assert.Single(detailSlots, slot => slot.GetProperty("isCaptain").GetBoolean());
        Assert.All(detailSlots, slot =>
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(slot.GetProperty("realTeamName").GetString()));
            Assert.True(slot.GetProperty("price").GetProperty("newPrice").GetDecimal() > 0);
        });
        Assert.Equal(
            detailSlots.Where(slot => slot.GetProperty("counts").GetBoolean())
                .Sum(slot => slot.GetProperty("points").GetDecimal())
                + detail.GetProperty("captainBonus").GetDecimal(),
            detail.GetProperty("total").GetDecimal());

        // Quem não entrou no campeonato vê a rodada, mas sem escalação nenhuma.
        var visitorEmail = UniqueEmail("publication-visitor");
        await CreateUserAsync(factory, visitorEmail);
        using var visitor = await CreateAuthenticatedClientAsync(factory, visitorEmail, cancellationToken);
        var visitorDetail = await visitor.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{world.RoundId}", cancellationToken);
        Assert.False(visitorDetail.GetProperty("played").GetBoolean());
        Assert.Empty(visitorDetail.GetProperty("slots").EnumerateArray());

        // A rodada consolida sozinha, pelo relógio, no fim da janela de correção.
        clock.Advance(TimeSpan.FromDays(10));
        var consolidated = await ReviewAsync(world, world.RoundId, cancellationToken);
        Assert.Equal("Consolidated", consolidated.GetProperty("phase").GetString());
        var afterConsolidation = await player.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{world.RoundId}", cancellationToken);
        Assert.False(afterConsolidation.GetProperty("provisional").GetBoolean());
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

    [Fact]
    public async Task CorrectingAPublishedRoundRewritesItAndEveryRoundAfterItInOneGo()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);
        var second = (await CreateRoundAsync(world.Owner, world, "Rodada 2", daysAhead: 5, cancellationToken))
            .GetProperty("id").GetGuid();

        var playerEmail = UniqueEmail("correction-player");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        await JoinWithCompleteSquadAsync(player, world.Slug, cancellationToken);

        var assistantEmail = UniqueEmail("correction-assistant");
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        await AddAssistantAsync(factory, world.OrganizationId, assistantId);

        clock.Advance(TimeSpan.FromDays(3));
        await OpenMarketAsync(world.Owner, world with { RoundId = second }, cancellationToken);
        clock.Advance(TimeSpan.FromDays(3));
        var sheet = await FillSheetAsync(world, world.RoundId, cancellationToken);
        await FillSheetAsync(world, second, cancellationToken);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);
        await PublishRoundAsync(world, second, cancellationToken);

        var firstTotal = await TotalAsync(player, world.Slug, world.RoundId, cancellationToken);
        var secondTotal = await TotalAsync(player, world.Slug, second, cancellationToken);

        // Passada a janela, a rodada consolidou: reabrir sem motivo é recusado. As sessões
        // de quem não organiza não sobrevivem a dez dias de relógio; elas são refeitas.
        clock.Advance(TimeSpan.FromDays(10));
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        using var reader = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        var consolidated = await ReviewAsync(world, world.RoundId, cancellationToken);
        Assert.Equal("Consolidated", consolidated.GetProperty("phase").GetString());
        var version = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using (var silent = await ReopenAsync(world.Owner, world, world.RoundId, version, null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, silent.StatusCode);
            Assert.Contains("já consolidou", await silent.Content.ReadAsStringAsync(cancellationToken));
        }

        var motive = "Gol lançado no atleta errado pela arbitragem.";
        using (var byAssistant = await ReopenAsync(assistant, world, world.RoundId, version, motive, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, byAssistant.StatusCode);
        }

        using (var reopened = await ReopenAsync(world.Owner, world, world.RoundId, version, motive, cancellationToken))
        {
            reopened.EnsureSuccessStatusCode();
            var review = await reopened.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal("ReopenedForCorrection", review.GetProperty("phase").GetString());
            Assert.Equal(motive, review.GetProperty("correction").GetProperty("reason").GetString());

            // A apuração vigente continua inteira: reabrir não desfaz nada.
            Assert.Equal(1, review.GetProperty("publication").GetProperty("revision").GetInt32());
        }

        // Quem joga continua lendo os números de antes, avisado de que a rodada mudou de mão.
        var during = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{world.RoundId}", cancellationToken);
        Assert.True(during.GetProperty("underCorrection").GetBoolean());
        Assert.Equal(1, during.GetProperty("revision").GetInt32());
        Assert.Equal(firstTotal, during.GetProperty("total").GetDecimal());
        Assert.Equal(
            secondTotal,
            (await TotalAsync(reader, world.Slug, second, cancellationToken))!.Value);

        // A rodada reaberta volta a aceitar súmula: o mandante fez 3, não 1.
        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 3);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var calculations = await dbContext.RoundCalculations
            .AsNoTracking()
            .Include(item => item.Athletes)
            .Include(item => item.Prices)
            .Where(item => item.CompetitionId == world.CompetitionId)
            .ToListAsync(cancellationToken);

        var corrected = calculations.Single(item => item.RoundId == world.RoundId && item.Revision == 2);
        Assert.Equal(motive, corrected.CorrectionReason);
        Assert.Equal(15m, corrected.Athletes.Single(item => item.AthleteId == sheet.Scorer).Points);
        Assert.Equal(5m, calculations
            .Single(item => item.RoundId == world.RoundId && item.Revision == 1)
            .Athletes.Single(item => item.AthleteId == sheet.Scorer).Points);

        // A Rodada 2 foi refeita na mesma transação, partindo dos preços novos da Rodada 1.
        var chained = calculations.Single(item => item.RoundId == second && item.Revision == 2);
        Assert.Contains("Rodada 1", chained.CorrectionReason ?? string.Empty, StringComparison.Ordinal);
        var afterCorrection = corrected.Prices.ToDictionary(item => (item.Kind, item.AssetId), item => item.NewPrice);
        Assert.All(
            chained.Prices,
            item => Assert.Equal(afterCorrection[(item.Kind, item.AssetId)], item.PreviousPrice));
        Assert.NotEqual(
            calculations.Single(item => item.RoundId == second && item.Revision == 1).Prices
                .Single(item => item.AssetId == sheet.Scorer).NewPrice,
            chained.Prices.Single(item => item.AssetId == sheet.Scorer).NewPrice);

        // O mercado vende pelo preço da última revisão da última rodada.
        var market = await MarketAsync(reader, world.Slug, cancellationToken);
        Assert.Equal(
            chained.Prices.Single(item => item.AssetId == sheet.Scorer).NewPrice,
            market.Single(item => Id(item) == sheet.Scorer).GetProperty("price").GetDecimal());

        // O participante lê o que mudou: motivo, quando saiu e quanto tinha antes.
        var after = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{world.RoundId}", cancellationToken);
        Assert.False(after.GetProperty("underCorrection").GetBoolean());
        Assert.Equal(2, after.GetProperty("revision").GetInt32());
        var correction = after.GetProperty("correction");
        Assert.Equal(motive, correction.GetProperty("reason").GetString());
        Assert.Equal(firstTotal, correction.GetProperty("previousTotal").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(correction.GetProperty("correctedAtLocal").GetString()));

        // A Rodada 2 também avisa, apontando a rodada que causou o recálculo.
        var chainedDetail = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{world.Slug}/rounds/{second}", cancellationToken);
        Assert.Contains(
            "Rodada 1",
            chainedDetail.GetProperty("correction").GetProperty("reason").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        var audit = await dbContext.AdministrativeAuditEntries
            .AsNoTracking()
            .Where(item => item.TargetId == world.RoundId)
            .ToListAsync(cancellationToken);
        Assert.Contains(audit, item =>
            item.Action == "CompetitionRoundReopenedForCorrection"
            && item.Reason.Contains(motive, StringComparison.Ordinal));
        Assert.Contains(audit, item =>
            item.Action == "CompetitionRoundPublished"
            && item.Reason.Contains("republicada com a revisão 2", StringComparison.Ordinal)
            && item.Reason.Contains("1 rodadas seguintes recalculadas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARoundBeingCorrectedHoldsBackEveryRoundThatComesAfterIt()
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
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var firstVersion = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using (var reopened = await ReopenAsync(
            world.Owner, world, world.RoundId, firstVersion, "Placar conferido errado na súmula.", cancellationToken))
        {
            reopened.EnsureSuccessStatusCode();
        }

        // A Rodada 2 não sai enquanto a anterior estiver em correção: os preços dela
        // partiriam de uma apuração que está prestes a mudar.
        var secondVersion = await SendToReviewAsync(world, second, cancellationToken);
        using var blocked = await PublishAsync(world.Owner, world, second, secondVersion, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("Publique antes a Rodada 1", await blocked.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task OnlyAPublishedRoundIsReopenedForCorrection()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var version = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using var refused = await ReopenAsync(
            world.Owner, world, world.RoundId, version, "Motivo mais do que suficiente.", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("competition_round_status", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublishingAndCorrectingLeaveOneNoticeEachInTheParticipantInbox()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var playerEmail = UniqueEmail("inbox-player");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        await JoinWithCompleteSquadAsync(player, world.Slug, cancellationToken);

        // Quem só tem conta não recebe aviso de um campeonato que não joga.
        var visitorEmail = UniqueEmail("inbox-visitor");
        await CreateUserAsync(factory, visitorEmail);
        using var visitor = await CreateAuthenticatedClientAsync(factory, visitorEmail, cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var inbox = await InboxAsync(player, cancellationToken);
        Assert.Equal(1, inbox.GetProperty("unread").GetInt32());
        var notice = Assert.Single(inbox.GetProperty("items").EnumerateArray());
        Assert.Equal("RoundPublished", notice.GetProperty("kind").GetString());
        Assert.Equal("Rodada 1 apurada", notice.GetProperty("title").GetString());
        Assert.Equal(world.RoundId, notice.GetProperty("roundId").GetGuid());
        Assert.Equal(world.Slug, notice.GetProperty("competitionSlug").GetString());
        Assert.False(notice.GetProperty("read").GetBoolean());
        Assert.Empty((await InboxAsync(visitor, cancellationToken)).GetProperty("items").EnumerateArray());

        // Publicar de novo responde sucesso sem apurar; também não avisa de novo.
        using (var again = await PublishAsync(
            world.Owner,
            world,
            world.RoundId,
            await RoundVersionAsync(world, world.RoundId, cancellationToken),
            cancellationToken))
        {
            again.EnsureSuccessStatusCode();
        }

        Assert.Equal(1, (await InboxAsync(player, cancellationToken)).GetProperty("unread").GetInt32());

        using (var read = await player.PostAsync("/api/v1/notifications/read", null, cancellationToken))
        {
            read.EnsureSuccessStatusCode();
            var updated = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(0, updated.GetProperty("unread").GetInt32());
            Assert.True(updated.GetProperty("items")[0].GetProperty("read").GetBoolean());
        }

        // A correção acrescenta um aviso novo, sem apagar o da publicação.
        clock.Advance(TimeSpan.FromDays(1));
        var version = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using (var reopened = await ReopenAsync(
            world.Owner, world, world.RoundId, version, "A súmula chegou com um gol a menos.", cancellationToken))
        {
            reopened.EnsureSuccessStatusCode();
        }

        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 3);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var depois = await InboxAsync(player, cancellationToken);
        Assert.Equal(1, depois.GetProperty("unread").GetInt32());
        var avisos = depois.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, avisos.Count);
        Assert.Equal("RoundCorrected", avisos[0].GetProperty("kind").GetString());
        Assert.Equal("Rodada 1 corrigida", avisos[0].GetProperty("title").GetString());
        Assert.Equal("RoundPublished", avisos[1].GetProperty("kind").GetString());
        Assert.True(avisos[1].GetProperty("read").GetBoolean());
    }

    private static async Task<JsonElement> InboxAsync(HttpClient client, CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications", cancellationToken);

    private static async Task<decimal?> TotalAsync(
        HttpClient client,
        string slug,
        Guid roundId,
        CancellationToken cancellationToken)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{slug}/rounds/{roundId}", cancellationToken);
        return detail.GetProperty("played").GetBoolean() ? detail.GetProperty("total").GetDecimal() : null;
    }

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

}
