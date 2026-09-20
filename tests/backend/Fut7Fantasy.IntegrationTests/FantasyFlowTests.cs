using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.FantasyScenario;
using static Fut7Fantasy.IntegrationTests.ImportRequests;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Adesão, mercado e escalação pela API real (Fase 9). O campeonato é a massa fictícia de
/// `infra/dados-demo`, publicada, com uma rodada marcada.
/// </summary>
public sealed class FantasyFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly string[] Positions = ["Goalkeeper", "Defender", "Midfielder", "Forward"];

    [Fact]
    public async Task ParticipantJoinsAndBuildsAValidSquadWhileTheServerEnforcesTheRules()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        var playerEmail = UniqueEmail("fantasy-player");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);

        var before = await OverviewAsync(player, world.Slug, cancellationToken);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("entry").ValueKind);
        Assert.False(before.GetProperty("market").GetProperty("isOpen").GetBoolean());
        Assert.Equal(6, before.GetProperty("teamLimit").GetProperty("activeRealTeams").GetInt32());
        Assert.Equal(3, before.GetProperty("teamLimit").GetProperty("maxStarters").GetInt32());

        var anyAthlete = (await MarketAsync(player, world.Slug, cancellationToken)).First();
        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, anyAthlete, cancellationToken), "fantasy_not_joined", cancellationToken);

        // Aderir credita o orçamento uma vez só, mesmo com o pedido repetido.
        using var joined = await player.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        joined.EnsureSuccessStatusCode();
        using var again = await player.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        var entry = (await again.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("entry");
        Assert.Equal(100m, entry.GetProperty("balance").GetDecimal());

        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, anyAthlete, cancellationToken),
            "fantasy_market_closed",
            cancellationToken);

        await OpenMarketAsync(world.Owner, world, cancellationToken);

        // Monta o elenco pelo mercado, sempre com o item mais barato que o servidor libera.
        foreach (var position in SquadPositions)
        {
            var choice = (await MarketAsync(player, world.Slug, cancellationToken))
                .Where(item => item.GetProperty("position").GetString() == position
                    && item.GetProperty("blockCode").ValueKind == JsonValueKind.Null)
                .MinBy(item => item.GetProperty("price").GetDecimal());
            using var bought = await BuyAsync(player, world.Slug, choice, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        }

        var coach = (await MarketAsync(player, world.Slug, cancellationToken))
            .Where(item => item.GetProperty("kind").GetString() == "Coach")
            .MinBy(item => item.GetProperty("price").GetDecimal());
        using var coachBought = await BuyAsync(player, world.Slug, coach, cancellationToken);
        coachBought.EnsureSuccessStatusCode();

        var squad = (await OverviewAsync(player, world.Slug, cancellationToken)).GetProperty("entry");
        var slots = squad.GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(12, slots.Count);
        Assert.Equal(["missing_captain"], Codes(squad.GetProperty("issues")));

        // Patrimônio é saldo mais o preço atual do elenco: comprar não o muda.
        Assert.Equal(100m, squad.GetProperty("patrimony").GetDecimal());

        var starterGoalkeeper = Slot(slots, "Goalkeeper", "Starter");
        var benchGoalkeeper = Slot(slots, "Goalkeeper", "Bench");
        await AssertRefusedAsync(
            await CaptainAsync(player, world.Slug, benchGoalkeeper, cancellationToken),
            "fantasy_invalid_captain",
            cancellationToken);
        using var captain = await CaptainAsync(player, world.Slug, starterGoalkeeper, cancellationToken);
        var complete = (await captain.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("entry");
        Assert.Empty(Codes(complete.GetProperty("issues")));

        // Mandar o capitão para o banco tira a braçadeira: não existe vice.
        using var swapped = await player.PutAsJsonAsync(
            Uri(world.Slug, "lineup/swap"),
            new { starterAthleteId = Id(starterGoalkeeper), benchAthleteId = Id(benchGoalkeeper) },
            cancellationToken);
        var afterSwap = (await swapped.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("entry");
        Assert.Equal(["missing_captain"], Codes(afterSwap.GetProperty("issues")));

        // O servidor recusa o que a tela deveria ter bloqueado.
        var extraGoalkeeper = (await MarketAsync(player, world.Slug, cancellationToken))
            .First(item => item.GetProperty("position").GetString() == "Goalkeeper"
                && !item.GetProperty("isOwned").GetBoolean());
        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, extraGoalkeeper, cancellationToken),
            "fantasy_position_full",
            cancellationToken);
        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, coach, cancellationToken), "fantasy_already_owned", cancellationToken);

        // Vender devolve o preço atual e reabre a vaga.
        using var sold = await player.DeleteAsync(
            Uri(world.Slug, $"squad/atleta/{Id(starterGoalkeeper)}"), cancellationToken);
        var afterSale = (await sold.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("entry");
        Assert.Contains("missing_bench", Codes(afterSale.GetProperty("issues")));
        Assert.Equal(100m, afterSale.GetProperty("patrimony").GetDecimal());

        // Depois do fechamento, pelo relógio do servidor, nada muda.
        clock.Advance(TimeSpan.FromDays(3));
        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, extraGoalkeeper, cancellationToken),
            "fantasy_market_closed",
            cancellationToken);
    }

    [Fact]
    public async Task ClosedMarketCreatesOneImmutableSnapshotAndLateEntryDoesNotReceiveIt()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var earlyEmail = UniqueEmail("fantasy-snapshot-early");
        await CreateUserAsync(factory, earlyEmail);
        using var early = await CreateAuthenticatedClientAsync(factory, earlyEmail, cancellationToken);
        var captainId = await JoinWithCompleteSquadAsync(early, world.Slug, cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));

        // A adesão tardia observa primeiro o fechamento e materializa quem já estava apto.
        var lateEmail = UniqueEmail("fantasy-snapshot-late");
        await CreateUserAsync(factory, lateEmail);
        using var late = await CreateAuthenticatedClientAsync(factory, lateEmail, cancellationToken);
        using var lateJoined = await late.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        lateJoined.EnsureSuccessStatusCode();

        // Repetir leituras não duplica o retrato.
        _ = await OverviewAsync(early, world.Slug, cancellationToken);
        _ = await OverviewAsync(late, world.Slug, cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var entries = await dbContext.FantasyEntries
            .AsNoTracking()
            .Where(entry => entry.CompetitionId == world.CompetitionId)
            .OrderBy(entry => entry.JoinedAt)
            .ToListAsync(cancellationToken);
        var snapshot = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Include(item => item.Slots)
            .SingleAsync(item => item.RoundId == world.RoundId, cancellationToken);
        var round = await dbContext.Rounds.AsNoTracking()
            .SingleAsync(item => item.Id == world.RoundId, cancellationToken);

        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].Id, snapshot.EntryId);
        Assert.NotEqual(entries[1].Id, snapshot.EntryId);
        Assert.Equal(round.MarketCloseAt, snapshot.MarketClosedAt);
        Assert.Equal(captainId, snapshot.CaptainAthleteId);
        Assert.Equal(12, snapshot.Slots.Count);
        Assert.All(snapshot.Slots, slot =>
        {
            Assert.NotEmpty(slot.AssetName);
            Assert.NotEmpty(slot.RealTeamName);
            Assert.True(slot.Price > 0);
        });
    }

    [Fact]
    public async Task OrganizerEditsAfterTheCloseDoNotReachTheSnapshotAndLateEntryPlaysTheNextRound()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var earlyEmail = UniqueEmail("fantasy-frozen-early");
        await CreateUserAsync(factory, earlyEmail);
        using var early = await CreateAuthenticatedClientAsync(factory, earlyEmail, cancellationToken);
        var captainId = await JoinWithCompleteSquadAsync(early, world.Slug, cancellationToken);

        var captain = (await world.Owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{world.CompetitionId}/athletes", cancellationToken))
            .EnumerateArray()
            .Single(athlete => athlete.GetProperty("id").GetGuid() == captainId);
        var originalName = captain.GetProperty("sportingName").GetString()!;
        var originalTeamName = captain.GetProperty("realTeamName").GetString()!;
        var captainTeamId = captain.GetProperty("realTeamId").GetGuid();

        clock.Advance(TimeSpan.FromDays(3));

        // Ninguém do fantasy agiu desde o fechamento: são as edições do organizador que
        // encontram a rodada fechada e precisam retratá-la antes de mudar o catálogo.
        var initialPriceOverride = captain.GetProperty("initialPriceOverride");
        using var renamedAthlete = await world.Owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/athletes/{captainId}",
            new
            {
                sportingName = $"{originalName} Renomeado",
                position = captain.GetProperty("position").GetString(),
                realTeamId = captainTeamId,
                priceTier = captain.GetProperty("priceTier").GetString(),
                initialPriceOverride = initialPriceOverride.ValueKind == JsonValueKind.Null
                    ? (decimal?)null
                    : initialPriceOverride.GetDecimal(),
                version = captain.GetProperty("version").GetString(),
            },
            cancellationToken);
        renamedAthlete.EnsureSuccessStatusCode();
        var team = (await world.Owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{world.CompetitionId}/teams", cancellationToken))
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == captainTeamId);
        using var renamedTeam = await world.Owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/teams/{captainTeamId}",
            new { name = $"{originalTeamName} FC", version = team.GetProperty("version").GetString() },
            cancellationToken);
        renamedTeam.EnsureSuccessStatusCode();

        // A entrada tardia monta o elenco durante o mercado da rodada seguinte.
        var secondRound = await CreateRoundAsync(world.Owner, world, "Rodada 2", daysAhead: 5, cancellationToken);
        var secondRoundId = secondRound.GetProperty("id").GetGuid();
        await OpenMarketAsync(world.Owner, world with { RoundId = secondRoundId }, cancellationToken);
        var lateEmail = UniqueEmail("fantasy-frozen-late");
        await CreateUserAsync(factory, lateEmail);
        using var late = await CreateAuthenticatedClientAsync(factory, lateEmail, cancellationToken);
        _ = await JoinWithCompleteSquadAsync(late, world.Slug, cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));
        _ = await OverviewAsync(late, world.Slug, cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var entries = await dbContext.FantasyEntries
            .AsNoTracking()
            .Where(entry => entry.CompetitionId == world.CompetitionId)
            .OrderBy(entry => entry.JoinedAt)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var snapshots = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Include(item => item.Slots)
            .Where(item => entries.Contains(item.EntryId))
            .ToListAsync(cancellationToken);

        Assert.Equal(2, entries.Count);
        var first = Assert.Single(snapshots, item => item.RoundId == world.RoundId);
        Assert.Equal(entries[0], first.EntryId);
        var frozenCaptain = first.Slots.Single(slot => slot.AssetId == captainId);
        Assert.Equal(originalName, frozenCaptain.AssetName);
        Assert.Equal(originalTeamName, frozenCaptain.RealTeamName);

        var second = snapshots.Where(item => item.RoundId == secondRoundId).ToList();
        Assert.Equal(entries.Order(), second.Select(item => item.EntryId).Order());
        var currentCaptain = second.Single(item => item.EntryId == entries[0])
            .Slots.Single(slot => slot.AssetId == captainId);
        Assert.Equal($"{originalName} Renomeado", currentCaptain.AssetName);
        Assert.Equal($"{originalTeamName} FC", currentCaptain.RealTeamName);
    }

    [Fact]
    public async Task ConcurrentPurchasesNeverSpendTheSameBalanceTwice()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);
        var playerEmail = UniqueEmail("fantasy-racer");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        using var joined = await player.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        joined.EnsureSuccessStatusCode();

        // Doze compras ao mesmo tempo, com o mesmo atleta repetido: cada uma leu o mesmo saldo.
        var market = await MarketAsync(player, world.Slug, cancellationToken);
        var targets = market.Where(item => item.GetProperty("kind").GetString() == "Athlete").Take(10)
            .Concat(Enumerable.Repeat(market.First(), 2))
            .ToList();
        var responses = await Task.WhenAll(
            targets.Select(item => BuyAsync(player, world.Slug, item, cancellationToken)));
        try
        {
            Assert.All(responses, response => Assert.Contains(
                response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

            var entry = (await OverviewAsync(player, world.Slug, cancellationToken)).GetProperty("entry");
            var slots = entry.GetProperty("slots").EnumerateArray().ToList();
            var spent = slots.Sum(slot => slot.GetProperty("purchasePrice").GetDecimal());

            // O que foi gravado bate com o saldo, e ninguém entrou duas vezes.
            Assert.Equal(100m - spent, entry.GetProperty("balance").GetDecimal());
            Assert.Equal(slots.Count, slots.Select(slot => slot.GetProperty("assetId").GetGuid()).Distinct().Count());
            Assert.Equal(responses.Count(response => response.StatusCode == HttpStatusCode.OK), slots.Count);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task SecondWriteFromAStaleReadOfTheEntryIsRefused()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        // O teste HTTP acima não garante que as requisições se intercalem. Aqui a
        // intercalação é forçada: duas leituras da mesma versão, duas compras, duas
        // gravações. É a versão da participação que impede gastar o mesmo saldo duas vezes.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        var playerEmail = UniqueEmail("fantasy-stale");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        using var joined = await player.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        joined.EnsureSuccessStatusCode();

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var firstEntry = await first.FantasyEntries.Include(entry => entry.Slots)
            .SingleAsync(entry => entry.CompetitionId == world.CompetitionId, cancellationToken);
        var secondEntry = await second.FantasyEntries.Include(entry => entry.Slots)
            .SingleAsync(entry => entry.CompetitionId == world.CompetitionId, cancellationToken);

        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);
        var rules = new SquadRules(profile, profile.RealTeamLimitFor(6));
        Assert.Null(firstEntry.Buy(
            new MarketAsset(AssetKind.Athlete, Guid.NewGuid(), Position.Forward, world.Teams[0], 9m, true),
            rules,
            ApiFactory.FixedNow));
        Assert.Null(secondEntry.Buy(
            new MarketAsset(AssetKind.Athlete, Guid.NewGuid(), Position.Forward, world.Teams[1], 9m, true),
            rules,
            ApiFactory.FixedNow));

        await first.SaveChangesAsync(cancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task OnlyOneRoundHasAnOpenMarketAndDraftsAreNotPlayable()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var second = await CreateRoundAsync(world.Owner, world, "Rodada 2", daysAhead: 9, cancellationToken);
        using var refused = await world.Owner.PutAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{second.GetProperty("id").GetGuid()}/status",
            new { transition = "OpenMarket", version = second.GetProperty("version").GetString() },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Só uma rodada", await refused.Content.ReadAsStringAsync(cancellationToken));

        // Quem está no modo demonstração vê, mas não adere.
        var viewerEmail = UniqueEmail("fantasy-viewer");
        await CreateUserAsync(factory, viewerEmail, "DemoViewer");
        using var viewer = await CreateAuthenticatedClientAsync(factory, viewerEmail, cancellationToken);
        using var overview = await viewer.GetAsync(Uri(world.Slug, string.Empty), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var join = await viewer.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, join.StatusCode);

        // Endereço que não é de campeonato publicado não existe para o jogo.
        using var unknown = await viewer.GetAsync(Uri("campeonato-que-nao-existe", string.Empty), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task OverviewTellsEachParticipantWhatCountedWhenTheMarketClosed()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var completeEmail = UniqueEmail("fantasy-closed-complete");
        await CreateUserAsync(factory, completeEmail);
        using var complete = await CreateAuthenticatedClientAsync(factory, completeEmail, cancellationToken);
        var captainId = await JoinWithCompleteSquadAsync(complete, world.Slug, cancellationToken);

        // Quem só comprou um atleta chega ao fechamento com a escalação incompleta.
        var incompleteEmail = UniqueEmail("fantasy-closed-incomplete");
        await CreateUserAsync(factory, incompleteEmail);
        using var incomplete = await CreateAuthenticatedClientAsync(factory, incompleteEmail, cancellationToken);
        using (var joined = await incomplete.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken))
        {
            joined.EnsureSuccessStatusCode();
        }

        var anyAthlete = (await MarketAsync(incomplete, world.Slug, cancellationToken))
            .First(item => item.GetProperty("kind").GetString() == "Athlete");
        using (var bought = await BuyAsync(incomplete, world.Slug, anyAthlete, cancellationToken))
        {
            bought.EnsureSuccessStatusCode();
        }

        // Enquanto nenhum mercado fechou, não há rodada fechada para contar.
        var open = (await OverviewAsync(complete, world.Slug, cancellationToken)).GetProperty("entry");
        Assert.Equal(JsonValueKind.Null, open.GetProperty("lastClosedRound").ValueKind);

        clock.Advance(TimeSpan.FromDays(3));

        var lateEmail = UniqueEmail("fantasy-closed-late");
        await CreateUserAsync(factory, lateEmail);
        using var late = await CreateAuthenticatedClientAsync(factory, lateEmail, cancellationToken);
        using (var lateJoined = await late.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken))
        {
            lateJoined.EnsureSuccessStatusCode();
        }

        var frozen = await LastClosedRoundAsync(complete, world.Slug, cancellationToken);
        Assert.Equal("Rodada 1", frozen.GetProperty("roundName").GetString());
        Assert.Equal("Frozen", frozen.GetProperty("status").GetString());
        Assert.Equal(captainId, frozen.GetProperty("captainAthleteId").GetGuid());
        var frozenSlots = frozen.GetProperty("slots").EnumerateArray().ToList();
        Assert.Equal(12, frozenSlots.Count);
        Assert.Single(frozenSlots, slot => slot.GetProperty("isCaptain").GetBoolean());
        Assert.Equal(7, frozenSlots.Count(slot => slot.GetProperty("role").GetString() == "Starter"));

        // O horário do fechamento vem no fuso do campeonato, para a tela não converter nada.
        var rounds = await world.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/rounds", cancellationToken);
        var closesAt = rounds.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == world.RoundId)
            .GetProperty("marketCloseAt").GetDateTimeOffset();
        Assert.Equal(closesAt, frozen.GetProperty("marketClosedAt").GetDateTimeOffset());
        Assert.Equal(
            closesAt.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            frozen.GetProperty("marketClosedAtLocal").GetString());

        var missed = await LastClosedRoundAsync(incomplete, world.Slug, cancellationToken);
        Assert.Equal("Incomplete", missed.GetProperty("status").GetString());
        Assert.Empty(missed.GetProperty("slots").EnumerateArray());

        var joinedLate = await LastClosedRoundAsync(late, world.Slug, cancellationToken);
        Assert.Equal("JoinedAfterClose", joinedLate.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, joinedLate.GetProperty("captainAthleteId").ValueKind);
    }

    [Fact]
    public async Task ServerRefusesEveryViolationWhenTheScreenIsBypassed()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        var catalog = (await world.Owner.GetFromJsonAsync<JsonElement>(
                $"/api/v1/competitions/{world.CompetitionId}/athletes", cancellationToken))
            .EnumerateArray()
            .ToList();

        // Quatro atletas de times diferentes no preço máximo, antes de o preço travar.
        var expensive = Positions
            .Select((position, index) => catalog.First(athlete =>
                athlete.GetProperty("position").GetString() == position
                && athlete.GetProperty("realTeamId").GetGuid() == world.Teams[index]))
            .ToList();
        foreach (var athlete in expensive)
        {
            using var priced = await world.Owner.PutAsJsonAsync(
                $"/api/v1/competitions/{world.CompetitionId}/athletes/{Id(athlete)}",
                new
                {
                    sportingName = athlete.GetProperty("sportingName").GetString(),
                    position = athlete.GetProperty("position").GetString(),
                    realTeamId = athlete.GetProperty("realTeamId").GetGuid(),
                    priceTier = athlete.GetProperty("priceTier").GetString(),
                    initialPriceOverride = 30m,
                    version = athlete.GetProperty("version").GetString(),
                },
                cancellationToken);
            priced.EnsureSuccessStatusCode();
        }

        await OpenMarketAsync(world.Owner, world, cancellationToken);

        // Orçamento: três compras de C$ 30 deixam C$ 10, e a quarta não cabe.
        var richEmail = UniqueEmail("fantasy-bypass-budget");
        await CreateUserAsync(factory, richEmail);
        using var rich = await CreateAuthenticatedClientAsync(factory, richEmail, cancellationToken);
        using (var joined = await rich.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken))
        {
            joined.EnsureSuccessStatusCode();
        }

        foreach (var athlete in expensive.Take(3))
        {
            using var bought = await BuyAthleteAsync(rich, world.Slug, Id(athlete), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        }

        using (var tooExpensive = await BuyAthleteAsync(rich, world.Slug, Id(expensive[3]), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, tooExpensive.StatusCode);
            var body = await tooExpensive.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal("fantasy_insufficient_balance", body.GetProperty("code").GetString());
            Assert.Equal("Faltam C$ 20,00.", body.GetProperty("detail").GetString());
        }

        var balance = (await OverviewAsync(rich, world.Slug, cancellationToken))
            .GetProperty("entry").GetProperty("balance").GetDecimal();
        Assert.Equal(10m, balance);

        // Limite por time, técnico único, troca, capitão, venda e ativo inexistente.
        var playerEmail = UniqueEmail("fantasy-bypass-rules");
        await CreateUserAsync(factory, playerEmail);
        using var player = await CreateAuthenticatedClientAsync(factory, playerEmail, cancellationToken);
        using (var joined = await player.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken))
        {
            joined.EnsureSuccessStatusCode();
        }

        var fromTeam = catalog
            .Where(athlete => athlete.GetProperty("realTeamId").GetGuid() == world.Teams[4])
            .ToList();
        var fiveFromTeam = new[]
            {
                ("Goalkeeper", 0), ("Goalkeeper", 1), ("Defender", 0), ("Defender", 1), ("Midfielder", 0),
            }
            .Select(pick => fromTeam
                .Where(athlete => athlete.GetProperty("position").GetString() == pick.Item1)
                .ElementAt(pick.Item2))
            .ToList();
        foreach (var athlete in fiveFromTeam)
        {
            using var bought = await BuyAthleteAsync(player, world.Slug, Id(athlete), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        }

        var sixth = fromTeam
            .Where(athlete => athlete.GetProperty("position").GetString() == "Midfielder")
            .ElementAt(1);
        await AssertRefusedAsync(
            await BuyAthleteAsync(player, world.Slug, Id(sixth), cancellationToken),
            "fantasy_team_limit",
            cancellationToken);

        var coaches = (await MarketAsync(player, world.Slug, cancellationToken))
            .Where(item => item.GetProperty("kind").GetString() == "Coach")
            .ToList();
        using (var firstCoach = await BuyAsync(player, world.Slug, coaches[0], cancellationToken))
        {
            firstCoach.EnsureSuccessStatusCode();
        }

        await AssertRefusedAsync(
            await BuyAsync(player, world.Slug, coaches[1], cancellationToken),
            "fantasy_coach_taken",
            cancellationToken);

        var slots = (await OverviewAsync(player, world.Slug, cancellationToken))
            .GetProperty("entry").GetProperty("slots").EnumerateArray().ToList();
        var starterGoalkeeper = Slot(slots, "Goalkeeper", "Starter");
        var benchGoalkeeper = Slot(slots, "Goalkeeper", "Bench");
        var starterDefender = Slot(slots, "Defender", "Starter");
        await AssertRefusedAsync(
            await SwapAsync(
                player, world.Slug, Id(starterGoalkeeper), Id(Slot(slots, "Midfielder", "Bench")), cancellationToken),
            "fantasy_invalid_swap",
            cancellationToken);
        await AssertRefusedAsync(
            await SwapAsync(player, world.Slug, Id(starterGoalkeeper), Id(sixth), cancellationToken),
            "fantasy_invalid_swap",
            cancellationToken);
        await AssertRefusedAsync(
            await CaptainAsync(player, world.Slug, sixth, cancellationToken),
            "fantasy_invalid_captain",
            cancellationToken);
        await AssertRefusedAsync(
            await player.DeleteAsync(Uri(world.Slug, $"squad/atleta/{Id(sixth)}"), cancellationToken),
            "fantasy_not_owned",
            cancellationToken);
        await AssertRefusedAsync(
            await BuyAthleteAsync(player, world.Slug, Guid.NewGuid(), cancellationToken),
            "fantasy_not_found",
            cancellationToken);
        using (var unknownKind = await player.PostAsync(
            Uri(world.Slug, $"squad/jogador/{Id(sixth)}"), null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownKind.StatusCode);
        }

        // Atleta desligado pelo organizador sai do mercado, mesmo por chamada direta.
        var released = catalog.First(athlete => athlete.GetProperty("realTeamId").GetGuid() == world.Teams[5]
            && athlete.GetProperty("position").GetString() == "Forward");
        using (var release = await world.Owner.DeleteAsync(
            $"/api/v1/competitions/{world.CompetitionId}/athletes/{Id(released)}", cancellationToken))
        {
            release.EnsureSuccessStatusCode();
        }

        await AssertRefusedAsync(
            await BuyAthleteAsync(player, world.Slug, Id(released), cancellationToken),
            "fantasy_unavailable",
            cancellationToken);

        // Depois do fechamento, nem troca, nem capitão, nem venda.
        clock.Advance(TimeSpan.FromDays(3));
        await AssertRefusedAsync(
            await SwapAsync(player, world.Slug, Id(starterGoalkeeper), Id(benchGoalkeeper), cancellationToken),
            "fantasy_market_closed",
            cancellationToken);
        await AssertRefusedAsync(
            await CaptainAsync(player, world.Slug, starterDefender, cancellationToken),
            "fantasy_market_closed",
            cancellationToken);
        await AssertRefusedAsync(
            await player.DeleteAsync(Uri(world.Slug, $"squad/atleta/{Id(starterDefender)}"), cancellationToken),
            "fantasy_market_closed",
            cancellationToken);

        // Nada do que foi recusado mudou o elenco: cinco atletas do mesmo time e o técnico.
        var after = (await OverviewAsync(player, world.Slug, cancellationToken)).GetProperty("entry");
        Assert.Equal(6, after.GetProperty("slots").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("captainAthleteId").ValueKind);
    }

    private static string[] Codes(JsonElement issues) =>
        [.. issues.EnumerateArray().Select(issue => issue.GetProperty("code").GetString()!)];

    private static JsonElement Slot(List<JsonElement> slots, string position, string role) =>
        slots.First(slot => slot.GetProperty("position").GetString() == position
            && slot.GetProperty("role").GetString() == role);

    private static Task<HttpResponseMessage> BuyAthleteAsync(
        HttpClient client,
        string slug,
        Guid athleteId,
        CancellationToken cancellationToken) =>
        client.PostAsync(Uri(slug, $"squad/atleta/{athleteId}"), null, cancellationToken);

    private static Task<HttpResponseMessage> SwapAsync(
        HttpClient client,
        string slug,
        Guid starterAthleteId,
        Guid benchAthleteId,
        CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(
            Uri(slug, "lineup/swap"),
            new { starterAthleteId, benchAthleteId },
            cancellationToken);

    private static async Task<JsonElement> LastClosedRoundAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        (await OverviewAsync(client, slug, cancellationToken))
            .GetProperty("entry")
            .GetProperty("lastClosedRound");

    private static async Task AssertRefusedAsync(
        HttpResponseMessage response,
        string code,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(code, body.GetProperty("code").GetString());
        }
    }

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
