using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>
/// Testes de ouro da escalação na rodada (01 §9): banco na mesma posição, capitão sem
/// herança e técnico pela média do time. As pontuações dos atletas entram prontas, para
/// que cada caso leia só a regra que testa.
/// </summary>
public sealed class LineupScoringTests
{
    private static readonly ScoringRuleSet Fut7 = ScoringRuleSets.CurrentFor(Modality.Fut7);
    private static readonly Guid Vila = Guid.NewGuid();
    private static readonly Guid Estrela = Guid.NewGuid();

    // Titulares do 1-2-2-2, um reserva por posição e o técnico da Vila.
    private static readonly FrozenLineupSlot Goleiro = Athlete("Pipoca", Position.Goalkeeper, SquadRole.Starter);
    private static readonly FrozenLineupSlot Zagueiro = Athlete("Baiano", Position.Defender, SquadRole.Starter);
    private static readonly FrozenLineupSlot Lateral = Athlete("Cascão", Position.Defender, SquadRole.Starter);
    private static readonly FrozenLineupSlot Volante = Athlete("Alemão", Position.Midfielder, SquadRole.Starter);
    private static readonly FrozenLineupSlot Meia = Athlete("Ceará", Position.Midfielder, SquadRole.Starter);
    private static readonly FrozenLineupSlot Ponta = Athlete("Chicão", Position.Forward, SquadRole.Starter);
    private static readonly FrozenLineupSlot Centroavante = Athlete("Zumbi", Position.Forward, SquadRole.Starter);
    private static readonly FrozenLineupSlot GoleiroReserva = Athlete("Gato", Position.Goalkeeper, SquadRole.Bench);
    private static readonly FrozenLineupSlot DefensorReserva = Athlete("Zeca", Position.Defender, SquadRole.Bench);
    private static readonly FrozenLineupSlot MeiaReserva = Athlete("Cebola", Position.Midfielder, SquadRole.Bench);
    private static readonly FrozenLineupSlot AtacanteReserva = Athlete("Jacaré", Position.Forward, SquadRole.Bench);
    private static readonly FrozenLineupSlot Tecnico = new(
        AssetKind.Coach, Guid.NewGuid(), "Seu Zé", null, Vila, SquadRole.Coach);

    private static readonly FrozenLineupSlot[] Elenco =
    [
        Goleiro, Zagueiro, Lateral, Volante, Meia, Ponta, Centroavante,
        GoleiroReserva, DefensorReserva, MeiaReserva, AtacanteReserva, Tecnico,
    ];

    [Fact]
    public void EveryoneJoinsTheTotalExceptTheBenchWhenAllStartersPlayed()
    {
        var athletes = Played(
            (Goleiro, 5m), (Zagueiro, 3m), (Lateral, 0m), (Volante, 4m), (Meia, 6m), (Ponta, 5m),
            (Centroavante, -2m), (GoleiroReserva, 9m), (DefensorReserva, 1m));

        var score = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));

        // Titulares 21 + capitão Chicão 5 a mais + técnico 2,5; reservas não entram.
        Assert.Equal(28.5m, score.Total);
        Assert.Equal(5m, score.CaptainBonus);
        Assert.False(Line(score, GoleiroReserva).Counts);
        Assert.True(Line(score, Tecnico).Counts);
    }

    [Fact]
    public void ReserveOnlyReplacesAStarterOfTheSamePositionWhoDidNotPlay()
    {
        // O goleiro titular não jogou e o reserva do gol jogou; o defensor reserva jogou,
        // mas ninguém da defesa faltou.
        var athletes = Played(
            (Zagueiro, 3m), (Lateral, 0m), (Volante, 4m), (Meia, 6m), (Ponta, 5m), (Centroavante, -2m),
            (GoleiroReserva, 9m), (DefensorReserva, 1m));

        var score = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));

        Assert.Equal(GoleiroReserva.AssetId, Line(score, Goleiro).ReplacedBy);
        Assert.Equal(Goleiro.AssetId, Line(score, GoleiroReserva).Replaces);
        Assert.True(Line(score, GoleiroReserva).Counts);
        Assert.False(Line(score, DefensorReserva).Counts);
        Assert.Equal(16m + 9m + 5m + 2.5m, score.Total);
    }

    [Fact]
    public void AnAbsentReserveCoversNobodyAndOtherPositionsCannotHelp()
    {
        // Goleiro e reserva do gol fora; o meia reserva jogou, mas não entra no gol.
        var athletes = Played(
            (Zagueiro, 3m), (Lateral, 0m), (Volante, 4m), (Meia, 6m), (Ponta, 5m), (Centroavante, -2m),
            (MeiaReserva, 7m));

        var score = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));

        Assert.Null(Line(score, Goleiro).ReplacedBy);
        Assert.False(Line(score, MeiaReserva).Counts);
        Assert.Equal(16m + 5m + 2.5m, score.Total);
    }

    [Fact]
    public void OneReservePerPositionCoversOnlyOneAbsentStarter()
    {
        // Os dois defensores titulares faltaram; o reserva cobre um só, sempre o mesmo.
        var athletes = Played(
            (Goleiro, 5m), (Volante, 4m), (Meia, 6m), (Ponta, 5m), (Centroavante, -2m), (DefensorReserva, 1m));

        var score = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));

        // Baiano vem antes de Cascão na ordem fixa pelo nome.
        Assert.Equal(DefensorReserva.AssetId, Line(score, Zagueiro).ReplacedBy);
        Assert.Null(Line(score, Lateral).ReplacedBy);
        Assert.Equal(18m + 1m + 5m + 2.5m, score.Total);
    }

    [Fact]
    public void CaptainWhoDidNotPlayGivesNoBonusAndTheReserveDoesNotInheritIt()
    {
        var athletes = Played(
            (Goleiro, 5m), (Zagueiro, 3m), (Lateral, 0m), (Volante, 4m), (Meia, 6m), (Centroavante, -2m),
            (AtacanteReserva, 10m));

        var score = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));

        Assert.Equal(0m, score.CaptainBonus);
        Assert.Equal(AtacanteReserva.AssetId, Line(score, Ponta).ReplacedBy);
        Assert.Equal(16m + 10m + 2.5m, score.Total);
    }

    [Fact]
    public void CaptainMultipliesNegativePointsToo()
    {
        var athletes = Played(
            (Goleiro, 5m), (Zagueiro, 3m), (Lateral, 0m), (Volante, 4m), (Meia, 6m), (Ponta, 5m),
            (Centroavante, -2m));

        var score = LineupScoring.Score(Fut7, Lineup(Centroavante), athletes, Coaches(2.5m));

        Assert.Equal(-2m, score.CaptainBonus);
        Assert.Equal(21m - 2m + 2.5m, score.Total);
    }

    [Fact]
    public void CoachWithoutAnyTeamAthleteOnTheFieldDoesNotScore()
    {
        var athletes = Played((Goleiro, 5m));

        var score = LineupScoring.Score(Fut7, Lineup(Goleiro), athletes, new Dictionary<Guid, decimal>());

        Assert.False(Line(score, Tecnico).Played);
        Assert.False(Line(score, Tecnico).Counts);
        Assert.Equal(0m, Line(score, Tecnico).Points);
        Assert.Equal(10m, score.Total);
    }

    [Fact]
    public void TheSameLineupAlwaysScoresTheSameWhateverTheSlotOrder()
    {
        var athletes = Played(
            (Goleiro, 5m), (Volante, 4m), (Meia, 6m), (Ponta, 5m), (Centroavante, -2m), (DefensorReserva, 1m),
            (GoleiroReserva, 3m));

        var first = LineupScoring.Score(Fut7, Lineup(Ponta), athletes, Coaches(2.5m));
        var shuffled = LineupScoring.Score(
            Fut7, new FrozenLineup(Ponta.AssetId, [.. Elenco.Reverse()]), athletes, Coaches(2.5m));

        Assert.Equal(first.Total, shuffled.Total);
        Assert.Equal(first.Lines, shuffled.Lines);
    }

    private static FrozenLineupSlot Athlete(string name, Position position, SquadRole role) =>
        new(AssetKind.Athlete, Guid.NewGuid(), name, position, name.Length % 2 == 0 ? Vila : Estrela, role);

    private static FrozenLineup Lineup(FrozenLineupSlot captain) => new(captain.AssetId, Elenco);

    private static Dictionary<Guid, AthleteRoundScore> Played(
        params (FrozenLineupSlot Slot, decimal Points)[] scores) =>
        scores.ToDictionary(
            score => score.Slot.AssetId,
            score => new AthleteRoundScore(
                score.Slot.AssetId, score.Slot.RealTeamId, score.Slot.Position!.Value, true, score.Points, []));

    private static Dictionary<Guid, decimal> Coaches(decimal vila) => new() { [Vila] = vila };

    private static LineupScoreLine Line(LineupRoundScore score, FrozenLineupSlot slot) =>
        score.Lines.Single(line => line.AssetId == slot.AssetId);
}
