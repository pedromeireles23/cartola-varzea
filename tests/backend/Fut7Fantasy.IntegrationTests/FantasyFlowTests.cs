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
using static Fut7Fantasy.IntegrationTests.ImportRequests;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Adesão, mercado e escalação pela API real (Fase 9). O campeonato é a massa fictícia de
/// `infra/dados-demo`, publicada, com uma rodada marcada.
/// </summary>
public sealed class FantasyFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly string[] SquadPositions =
    [
        "Goalkeeper", "Goalkeeper", "Defender", "Defender", "Defender", "Midfielder", "Midfielder",
        "Midfielder", "Forward", "Forward", "Forward",
    ];

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
        using var earlyJoined = await early.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        earlyJoined.EnsureSuccessStatusCode();

        foreach (var position in SquadPositions)
        {
            var choice = (await MarketAsync(early, world.Slug, cancellationToken))
                .Where(item => item.GetProperty("position").GetString() == position
                    && item.GetProperty("blockCode").ValueKind == JsonValueKind.Null)
                .MinBy(item => item.GetProperty("price").GetDecimal());
            using var bought = await BuyAsync(early, world.Slug, choice, cancellationToken);
            bought.EnsureSuccessStatusCode();
        }

        var coach = (await MarketAsync(early, world.Slug, cancellationToken))
            .Where(item => item.GetProperty("kind").GetString() == "Coach")
            .MinBy(item => item.GetProperty("price").GetDecimal());
        using var coachBought = await BuyAsync(early, world.Slug, coach, cancellationToken);
        coachBought.EnsureSuccessStatusCode();

        var slots = (await OverviewAsync(early, world.Slug, cancellationToken))
            .GetProperty("entry").GetProperty("slots").EnumerateArray().ToList();
        var captainSlot = slots.First(slot => slot.GetProperty("role").GetString() == "Starter");
        using var captain = await CaptainAsync(early, world.Slug, captainSlot, cancellationToken);
        captain.EnsureSuccessStatusCode();

        clock.Advance(TimeSpan.FromDays(3));

        // A adesÃ£o tardia observa primeiro o fechamento e materializa quem jÃ¡ estava apto.
        var lateEmail = UniqueEmail("fantasy-snapshot-late");
        await CreateUserAsync(factory, lateEmail);
        using var late = await CreateAuthenticatedClientAsync(factory, lateEmail, cancellationToken);
        using var lateJoined = await late.PostAsync(Uri(world.Slug, "entry"), null, cancellationToken);
        lateJoined.EnsureSuccessStatusCode();

        // Repetir leituras nÃ£o duplica o retrato.
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
        Assert.Equal(Id(captainSlot), snapshot.CaptainAthleteId);
        Assert.Equal(12, snapshot.Slots.Count);
        Assert.All(snapshot.Slots, slot =>
        {
            Assert.NotEmpty(slot.AssetName);
            Assert.NotEmpty(slot.RealTeamName);
            Assert.True(slot.Price > 0);
        });
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

    private static Uri Uri(string slug, string path) =>
        new($"/api/v1/fantasy/{slug}/{path}", UriKind.Relative);

    private static Guid Id(JsonElement item) =>
        item.TryGetProperty("assetId", out var assetId) ? assetId.GetGuid() : item.GetProperty("id").GetGuid();

    private static string[] Codes(JsonElement issues) =>
        [.. issues.EnumerateArray().Select(issue => issue.GetProperty("code").GetString()!)];

    private static JsonElement Slot(List<JsonElement> slots, string position, string role) =>
        slots.First(slot => slot.GetProperty("position").GetString() == position
            && slot.GetProperty("role").GetString() == role);

    private static async Task<JsonElement> OverviewAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<JsonElement>(Uri(slug, string.Empty), cancellationToken);

    private static async Task<List<JsonElement>> MarketAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        [
            .. (await client.GetFromJsonAsync<JsonElement>(Uri(slug, "market"), cancellationToken))
                .GetProperty("items").EnumerateArray(),
        ];

    private static Task<HttpResponseMessage> BuyAsync(
        HttpClient client,
        string slug,
        JsonElement item,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            Uri(slug, $"squad/{(item.GetProperty("kind").GetString() == "Coach" ? "tecnico" : "atleta")}/{Id(item)}"),
            null,
            cancellationToken);

    private static Task<HttpResponseMessage> CaptainAsync(
        HttpClient client,
        string slug,
        JsonElement slot,
        CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(Uri(slug, "lineup/captain"), new { athleteId = Id(slot) }, cancellationToken);

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

    /// <summary>Massa de demonstração publicada, com a Rodada 1 marcada para daqui a dois dias.</summary>
    private static async Task<World> BuildAsync(
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

        var world = new World(owner, competitionId, slug, stage.GetProperty("id").GetGuid(), teams, Guid.Empty);
        var round = await CreateRoundAsync(owner, world, "Rodada 1", daysAhead: 2, cancellationToken);
        return world with { RoundId = round.GetProperty("id").GetGuid() };
    }

    private static async Task<JsonElement> CreateRoundAsync(
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

    private static async Task OpenMarketAsync(HttpClient owner, World world, CancellationToken cancellationToken)
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

    private sealed record World(
        HttpClient Owner,
        Guid CompetitionId,
        string Slug,
        Guid StageId,
        List<Guid> Teams,
        Guid RoundId);
}
