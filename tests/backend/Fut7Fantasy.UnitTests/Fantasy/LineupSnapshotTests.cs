using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Fantasy;

public sealed class LineupSnapshotTests
{
    private static readonly DateTimeOffset JoinedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MarketClosedAt = JoinedAt.AddHours(1);

    [Fact]
    public void CompleteLineupCapturesCatalogLabelsPricesCaptainAndRules()
    {
        var (entry, rules, assets) = CompleteEntry(JoinedAt);

        var snapshot = LineupSnapshot.Capture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            entry,
            rules,
            assets,
            MarketClosedAt,
            MarketClosedAt.AddMinutes(5));

        Assert.Equal(entry.Id, snapshot.EntryId);
        Assert.Equal(entry.CaptainAthleteId, snapshot.CaptainAthleteId);
        Assert.Equal(rules.Profile.Version, snapshot.ModalityProfileVersion);
        Assert.Equal(rules.TeamLimit.MaxStarters, snapshot.MaxStartersPerTeam);
        Assert.Equal(rules.TeamLimit.MaxAthletes, snapshot.MaxAthletesPerTeam);
        Assert.Equal(entry.Slots.Count, snapshot.Slots.Count);
        Assert.All(snapshot.Slots, slot =>
        {
            var asset = assets[(slot.Kind, slot.AssetId)];
            Assert.Equal(asset.Name, slot.AssetName);
            Assert.Equal(asset.RealTeamName, slot.RealTeamName);
            Assert.Equal(asset.Price, slot.Price);
        });
    }

    [Fact]
    public void EntryJoinedAfterMarketCloseCannotReceiveRetroactiveSnapshot()
    {
        var (entry, rules, assets) = CompleteEntry(MarketClosedAt.AddSeconds(1));

        var exception = Assert.Throws<InvalidOperationException>(() => LineupSnapshot.Capture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            entry,
            rules,
            assets,
            MarketClosedAt,
            MarketClosedAt.AddMinutes(5)));

        Assert.Contains("depois do fechamento", exception.Message);
    }

    [Fact]
    public void IncompleteLineupDoesNotBecomeSnapshot()
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);
        var entry = FantasyEntry.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), profile, JoinedAt);

        var exception = Assert.Throws<InvalidOperationException>(() => LineupSnapshot.Capture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            entry,
            new SquadRules(profile, profile.RealTeamLimitFor(4)),
            new Dictionary<(AssetKind, Guid), SnapshotAsset>(),
            MarketClosedAt,
            MarketClosedAt));

        Assert.Contains("escalação completa", exception.Message);
    }

    private static (FantasyEntry Entry, SquadRules Rules, Dictionary<(AssetKind, Guid), SnapshotAsset> Assets)
        CompleteEntry(DateTimeOffset joinedAt)
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);
        var rules = new SquadRules(profile, profile.RealTeamLimitFor(4));
        var entry = FantasyEntry.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), profile, joinedAt);
        var teams = Enumerable.Range(1, 4).Select(_ => Guid.NewGuid()).ToArray();
        Dictionary<(AssetKind, Guid), SnapshotAsset> assets = [];
        var index = 0;

        foreach (var position in Enum.GetValues<Position>())
        {
            for (var slot = 0; slot < profile.Formation.CountOf(position) + 1; slot++)
            {
                var id = Guid.NewGuid();
                var team = teams[index % teams.Length];
                var price = profile.InitialAthletePrice(position, PriceTier.Basic, null);
                var market = new MarketAsset(AssetKind.Athlete, id, position, team, price, true);
                Assert.Null(entry.Buy(market, rules, joinedAt));
                assets[(AssetKind.Athlete, id)] = new(
                    AssetKind.Athlete,
                    id,
                    $"Atleta {index + 1}",
                    position,
                    team,
                    $"Time {(index % teams.Length) + 1}",
                    price);
                index++;
            }
        }

        var coachId = Guid.NewGuid();
        var coachPrice = profile.InitialCoachPrice(PriceTier.Basic, null);
        Assert.Null(entry.Buy(
            new MarketAsset(AssetKind.Coach, coachId, null, teams[0], coachPrice, true),
            rules,
            joinedAt));
        assets[(AssetKind.Coach, coachId)] = new(
            AssetKind.Coach,
            coachId,
            "Técnico do Time 1",
            null,
            teams[0],
            "Time 1",
            coachPrice);

        var captain = entry.Slots.First(slot => slot.Role == SquadRole.Starter).AssetId;
        Assert.Null(entry.SetCaptain(captain, joinedAt));
        Assert.Empty(entry.Issues(rules));
        return (entry, rules, assets);
    }
}
