using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>
/// A apuração inteira de uma rodada pequena, com os números conferidos à mão: Vila 1 × 0
/// Estrela, gol de defensor da Vila.
/// </summary>
public sealed class RoundCalculationTests
{
    private static readonly ScoringRuleSet Fut7 = ScoringRuleSets.CurrentFor(Modality.Fut7);
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Vila = Guid.NewGuid();
    private static readonly Guid Estrela = Guid.NewGuid();
    private static readonly Guid Match = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();

    private static readonly Guid GoleiroVila = Guid.NewGuid();
    private static readonly Guid DefensorVila = Guid.NewGuid();
    private static readonly Guid AtacanteVila = Guid.NewGuid();
    private static readonly Guid MeiaVila = Guid.NewGuid();
    private static readonly Guid GoleiroEstrela = Guid.NewGuid();
    private static readonly Guid AtacanteEstrela = Guid.NewGuid();
    private static readonly Guid MeiaEstrela = Guid.NewGuid();
    private static readonly Guid TecnicoVila = Guid.NewGuid();
    private static readonly Guid TecnicoEstrela = Guid.NewGuid();
    private static readonly Guid Participacao = Guid.NewGuid();

    [Fact]
    public void RoundIsScoredForAthletesCoachesEntriesAndPricesFromTheSameFacts()
    {
        var calculation = Compute(Performances(), Catalog(), [Lineup()]);

        // Atletas: goleiro da Vila sem sofrer gol 5; defensor com gol e sem sofrer gol 13;
        // goleiro da Estrela sofreu um gol -1; atacante da Estrela com amarelo -2.
        Assert.Equal(5m, Points(calculation, GoleiroVila));
        Assert.Equal(13m, Points(calculation, DefensorVila));
        Assert.Equal(-1m, Points(calculation, GoleiroEstrela));
        Assert.Equal(-2m, Points(calculation, AtacanteEstrela));
        Assert.False(calculation.Athletes.Single(item => item.AthleteId == MeiaVila).Played);
        Assert.Equal(
            [(ScoringItem.Goal, 8m), (ScoringItem.CleanSheet, 5m)],
            calculation.AthleteLines
                .Where(line => line.AthleteId == DefensorVila)
                .Select(line => (line.Item, line.Points)));

        // Técnicos: média de quem jogou em cada time.
        Assert.Equal(6m, calculation.Coaches.Single(item => item.CoachId == TecnicoVila).Points);
        Assert.Equal(-1.5m, calculation.Coaches.Single(item => item.CoachId == TecnicoEstrela).Points);

        // Participação: 5 + 13 - 2 dos titulares, 13 a mais do capitão e 6 do técnico.
        var entry = Assert.Single(calculation.Entries);
        Assert.Equal(35m, entry.Total);
        Assert.Equal(13m, entry.CaptainBonus);
        Assert.Equal(5, calculation.EntrySlots.Count);

        // Preços: goleiros média 2; atacantes média -1; técnicos média 2,25; o meia que
        // não jogou e o que nem estava na súmula ficam como estavam.
        Assert.Equal(8m, NewPrice(calculation, GoleiroVila));
        Assert.Equal(6m, NewPrice(calculation, GoleiroEstrela));
        Assert.Equal(7m, NewPrice(calculation, DefensorVila));
        Assert.Equal(7.5m, NewPrice(calculation, AtacanteVila));
        Assert.Equal(6.5m, NewPrice(calculation, AtacanteEstrela));
        Assert.Equal(7m, NewPrice(calculation, MeiaVila));
        Assert.Equal(7m, NewPrice(calculation, MeiaEstrela));
        Assert.Equal(8m, NewPrice(calculation, TecnicoVila));
        Assert.Equal(6m, NewPrice(calculation, TecnicoEstrela));
        Assert.Equal(Catalog().Length, calculation.Prices.Count);

        Assert.Equal(
            [
                (AssetKind.Athlete, (Position?)Position.Goalkeeper, 2m),
                (AssetKind.Athlete, (Position?)Position.Defender, 13m),
                (AssetKind.Athlete, (Position?)Position.Forward, -1m),
                (AssetKind.Coach, (Position?)null, 2.25m),
            ],
            calculation.Averages.Select(item => (item.Kind, item.Position, item.Average)));
        Assert.Equal(1, calculation.ScoringRuleSetVersion);
    }

    [Fact]
    public void SameFactsInAnyOrderGiveTheSameCalculation()
    {
        var first = Compute(Performances(), Catalog(), [Lineup()]);
        var second = Compute([.. Performances().Reverse()], [.. Catalog().Reverse()], [Lineup(reversed: true)]);

        Assert.Equal(Project(first), Project(second));
    }

    [Fact]
    public void RevisionStartsAtOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundCalculation.Compute(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Fut7, [], [], [], Now, Organizer));
    }

    private static RoundCalculation Compute(
        IEnumerable<MatchPerformance> performances,
        IReadOnlyCollection<PricedAsset> catalog,
        IEnumerable<EntryLineup> lineups) =>
        RoundCalculation.Compute(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Fut7, performances, catalog, lineups, Now, Organizer);

    private static MatchPerformance[] Performances() =>
    [
        Played(GoleiroVila, Vila, Position.Goalkeeper, conceded: 0) with
        {
            Appearance = Appearance(GoleiroVila, Vila, Position.Goalkeeper) with { PlayedAsGoalkeeper = true },
        },
        Played(DefensorVila, Vila, Position.Defender, conceded: 0) with
        {
            Appearance = Appearance(DefensorVila, Vila, Position.Defender) with { Goals = 1 },
        },
        Played(AtacanteVila, Vila, Position.Forward, conceded: 0),
        new(Match, 0, Appearance(MeiaVila, Vila, Position.Midfielder) with { DidPlay = false }),
        Played(GoleiroEstrela, Estrela, Position.Goalkeeper, conceded: 1) with
        {
            Appearance = Appearance(GoleiroEstrela, Estrela, Position.Goalkeeper) with
            {
                PlayedAsGoalkeeper = true,
                GoalsConceded = 1,
            },
        },
        Played(AtacanteEstrela, Estrela, Position.Forward, conceded: 1) with
        {
            Appearance = Appearance(AtacanteEstrela, Estrela, Position.Forward) with { YellowCards = 1 },
        },
    ];

    private static PricedAsset[] Catalog() =>
    [
        new(AssetKind.Athlete, GoleiroVila, Position.Goalkeeper, Vila, 7m),
        new(AssetKind.Athlete, DefensorVila, Position.Defender, Vila, 7m),
        new(AssetKind.Athlete, AtacanteVila, Position.Forward, Vila, 7m),
        new(AssetKind.Athlete, MeiaVila, Position.Midfielder, Vila, 7m),
        new(AssetKind.Athlete, GoleiroEstrela, Position.Goalkeeper, Estrela, 7m),
        new(AssetKind.Athlete, AtacanteEstrela, Position.Forward, Estrela, 7m),
        new(AssetKind.Athlete, MeiaEstrela, Position.Midfielder, Estrela, 7m),
        new(AssetKind.Coach, TecnicoVila, null, Vila, 7m),
        new(AssetKind.Coach, TecnicoEstrela, null, Estrela, 7m),
    ];

    private static EntryLineup Lineup(bool reversed = false)
    {
        FrozenLineupSlot[] slots =
        [
            new(AssetKind.Athlete, GoleiroVila, "Pipoca", Position.Goalkeeper, Vila, SquadRole.Starter),
            new(AssetKind.Athlete, DefensorVila, "Baiano", Position.Defender, Vila, SquadRole.Starter),
            new(AssetKind.Athlete, AtacanteEstrela, "Chicão", Position.Forward, Estrela, SquadRole.Starter),
            new(AssetKind.Athlete, AtacanteVila, "Jacaré", Position.Forward, Vila, SquadRole.Bench),
            new(AssetKind.Coach, TecnicoVila, "Seu Zé", null, Vila, SquadRole.Coach),
        ];
        return new(Participacao, new FrozenLineup(DefensorVila, reversed ? [.. slots.Reverse()] : slots));
    }

    private static MatchPerformance Played(Guid athlete, Guid team, Position position, int conceded) =>
        new(Match, conceded, Appearance(athlete, team, position));

    private static MatchSheetAppearanceDefinition Appearance(Guid athlete, Guid team, Position position) =>
        new(athlete, team, position, true, false, 0, 0, 0, 0, 0, 0, 0, null, 0, 0);

    private static decimal Points(RoundCalculation calculation, Guid athlete) =>
        calculation.Athletes.Single(item => item.AthleteId == athlete).Points;

    private static decimal NewPrice(RoundCalculation calculation, Guid asset) =>
        calculation.Prices.Single(item => item.AssetId == asset).NewPrice;

    /// <summary>Tudo o que a apuração decidiu, sem os identificadores das linhas.</summary>
    private static string Project(RoundCalculation calculation) => string.Join(
        '\n',
        [
            .. calculation.Athletes.Select(item => $"A {item.AthleteId} {item.Played} {item.Points}"),
            .. calculation.AthleteLines.Select(item => $"L {item.AthleteId} {item.Item} {item.Quantity} {item.Points}"),
            .. calculation.Coaches.Select(item => $"C {item.CoachId} {item.Played} {item.Points}"),
            .. calculation.Entries.Select(item => $"E {item.EntryId} {item.Total} {item.CaptainBonus}"),
            .. calculation.EntrySlots.Select(item =>
                $"S {item.AssetId} {item.Role} {item.Played} {item.Points} {item.Counts} {item.ReplacedBy}"),
            .. calculation.Averages.Select(item => $"M {item.Kind} {item.Position} {item.Average}"),
            .. calculation.Prices.Select(item =>
                $"P {item.AssetId} {item.PreviousPrice} {item.Variation} {item.NewPrice}"),
        ]);
}
