using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Fantasy;

public sealed class FantasyEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Aurora = Guid.NewGuid();
    private static readonly Guid Estrela = Guid.NewGuid();
    private static readonly Guid Brisa = Guid.NewGuid();
    private static readonly Guid Vila = Guid.NewGuid();

    [Theory]
    [InlineData(Modality.Fut7, 100)]
    [InlineData(Modality.Futsal, 80)]
    [InlineData(Modality.Field, 135)]
    public void JoiningCreditsTheModalityBudget(Modality modality, decimal budget)
    {
        var entry = Join(modality);

        Assert.Equal(budget, entry.Balance);
        Assert.Empty(entry.Slots);
    }

    [Fact]
    public void BuyingFillsStartersFirstThenTheBenchThenRefuses()
    {
        var entry = Join();
        var rules = Rules(activeTeams: 4);

        Assert.Null(entry.Buy(Athlete(Position.Goalkeeper, Aurora), rules, Now));
        Assert.Null(entry.Buy(Athlete(Position.Goalkeeper, Estrela), rules, Now));
        var third = entry.Buy(Athlete(Position.Goalkeeper, Brisa), rules, Now);

        Assert.Equal([SquadRole.Starter, SquadRole.Bench], entry.Slots.Select(slot => slot.Role));
        Assert.Equal("position_full", third?.Code);
        Assert.Equal(100m - 14m, entry.Balance);
    }

    [Fact]
    public void BuyingNeverSpendsMoreThanTheBalanceAndSaysHowMuchIsMissing()
    {
        var entry = Join(Modality.Futsal);
        var rules = Rules(activeTeams: 4, Modality.Futsal);
        foreach (var (position, team) in new[]
        {
            (Position.Goalkeeper, Aurora), (Position.Defender, Estrela), (Position.Midfielder, Brisa),
            (Position.Midfielder, Vila), (Position.Forward, Aurora), (Position.Goalkeeper, Estrela),
            (Position.Defender, Brisa),
        })
        {
            Assert.Null(entry.Buy(Athlete(position, team, price: 11m), rules, Now));
        }

        // 80 - 7 x 11 = 3 créditos sobrando.
        var refused = entry.Buy(Athlete(Position.Midfielder, Aurora, price: 5.5m), rules, Now);

        Assert.Equal("insufficient_balance", refused?.Code);
        Assert.Equal("Faltam C$ 2,50.", refused?.Message);
        Assert.Equal(3m, entry.Balance);
    }

    [Fact]
    public void SameAssetIsNeverBoughtTwice()
    {
        var entry = Join();
        var bia = Athlete(Position.Forward, Aurora);

        Assert.Null(entry.Buy(bia, Rules(4), Now));
        Assert.Equal("already_owned", entry.Buy(bia, Rules(4), Now)?.Code);
        Assert.Single(entry.Slots);
    }

    [Fact]
    public void UnavailableAssetIsNotBoughtButCanBeSold()
    {
        var entry = Join();
        var bia = Athlete(Position.Forward, Aurora);
        Assert.Null(entry.Buy(bia, Rules(4), Now));

        Assert.Equal("unavailable", entry.Buy(bia with { Id = Guid.NewGuid(), IsAvailable = false }, Rules(4), Now)?.Code);

        // Desligado ou eliminado sai pelo preço atual, que pode ter mudado.
        Assert.Null(entry.Sell(AssetKind.Athlete, bia.Id, currentPrice: 7.5m, Now));
        Assert.Equal(100m - 7m + 7.5m, entry.Balance);
    }

    [Theory]
    [InlineData(4, 3, 5)]
    [InlineData(3, 4, 6)]
    [InlineData(2, 5, 8)]
    public void TeamLimitFollowsHowManyTeamsAreStillActiveInFut7(int activeTeams, int maxStarters, int maxAthletes)
    {
        var entry = Join();
        var rules = Rules(activeTeams);
        var positions = new[]
        {
            Position.Goalkeeper, Position.Defender, Position.Defender, Position.Midfielder, Position.Midfielder,
            Position.Forward, Position.Forward, Position.Goalkeeper, Position.Defender, Position.Midfielder,
            Position.Forward,
        };

        var bought = 0;
        foreach (var position in positions)
        {
            if (entry.Buy(Athlete(position, Aurora, price: 1m), rules, Now) is null)
            {
                bought++;
            }
        }

        Assert.Equal(maxAthletes, bought);
        Assert.Equal(maxStarters, entry.Slots.Count(slot => slot.Role == SquadRole.Starter));
    }

    [Fact]
    public void CaptainMustBeAStarterAndLeavesWithTheBenchSwap()
    {
        var entry = Join();
        var rules = Rules(4);
        var starter = Athlete(Position.Forward, Aurora);
        var second = Athlete(Position.Forward, Estrela);
        var bench = Athlete(Position.Forward, Brisa);
        Assert.Null(entry.Buy(starter, rules, Now));
        Assert.Null(entry.Buy(second, rules, Now));
        Assert.Null(entry.Buy(bench, rules, Now));

        Assert.Equal("invalid_captain", entry.SetCaptain(bench.Id, Now)?.Code);
        Assert.Null(entry.SetCaptain(starter.Id, Now));

        Assert.Null(entry.Swap(starter.Id, bench.Id, rules, Now));

        // Capitão que vai para o banco perde a braçadeira: não existe vice para herdá-la.
        Assert.Null(entry.CaptainAthleteId);
        Assert.Equal(
            SquadRole.Starter,
            entry.Slots.Single(slot => slot.AssetId == bench.Id).Role);
    }

    [Fact]
    public void SwapOnlyHappensInsideTheSamePosition()
    {
        var entry = Join();
        var rules = Rules(4);
        var goalkeeper = Athlete(Position.Goalkeeper, Aurora);
        var benchGoalkeeper = Athlete(Position.Goalkeeper, Estrela);
        var forward = Athlete(Position.Forward, Brisa);
        Assert.Null(entry.Buy(goalkeeper, rules, Now));
        Assert.Null(entry.Buy(benchGoalkeeper, rules, Now));
        Assert.Null(entry.Buy(forward, rules, Now));

        Assert.Equal("invalid_swap", entry.Swap(forward.Id, benchGoalkeeper.Id, rules, Now)?.Code);
    }

    [Theory]
    [InlineData(Modality.Fut7)]
    [InlineData(Modality.Futsal)]
    [InlineData(Modality.Field)]
    public void CheapestFullSquadIsValidInsideTheModalityBudget(Modality modality)
    {
        var entry = Join(modality);
        var rules = Rules(activeTeams: 4, modality);
        var teams = new[] { Aurora, Estrela, Brisa, Vila };
        var formation = rules.Profile.Formation;
        var index = 0;
        foreach (var position in Enum.GetValues<Position>())
        {
            for (var slot = 0; slot < formation.CountOf(position) + 1; slot++)
            {
                var basic = rules.Profile.InitialAthletePrice(position, PriceTier.Basic, null);
                Assert.Null(entry.Buy(Athlete(position, teams[index++ % teams.Length], basic), rules, Now));
            }
        }

        Assert.Null(entry.Buy(
            new MarketAsset(AssetKind.Coach, Guid.NewGuid(), null, Aurora, 5.5m, true), rules, Now));
        var captain = entry.Slots.First(slot => slot.Role == SquadRole.Starter).AssetId;
        Assert.Null(entry.SetCaptain(captain, Now));

        Assert.Empty(entry.Issues(rules));
        Assert.True(entry.Balance >= 0);
    }

    [Fact]
    public void IncompleteSquadListsWhatIsMissing()
    {
        var entry = Join();

        var issues = entry.Issues(Rules(4)).Select(issue => issue.Code).ToList();

        Assert.Contains("missing_starter", issues);
        Assert.Contains("missing_bench", issues);
        Assert.Contains("missing_coach", issues);
        Assert.Contains("missing_captain", issues);
    }

    private static FantasyEntry Join(Modality modality = Modality.Fut7) =>
        FantasyEntry.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ModalityProfiles.CurrentFor(modality), Now);

    private static SquadRules Rules(int activeTeams, Modality modality = Modality.Fut7)
    {
        var profile = ModalityProfiles.CurrentFor(modality);
        return new SquadRules(profile, profile.RealTeamLimitFor(activeTeams));
    }

    private static MarketAsset Athlete(Position position, Guid team, decimal price = 7m) =>
        new(AssetKind.Athlete, Guid.NewGuid(), position, team, price, true);
}
