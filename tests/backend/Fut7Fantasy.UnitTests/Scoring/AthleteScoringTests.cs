using System.Globalization;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>
/// Testes de ouro da pontuação por atleta (01 §9, "Regras de aplicação"). Os valores
/// esperados são escritos à mão a partir da tabela aprovada, nunca calculados pelo
/// próprio código.
/// </summary>
public sealed class AthleteScoringTests
{
    private static readonly ScoringRuleSet Fut7 = ScoringRuleSets.CurrentFor(Modality.Fut7);

    private static readonly Guid Team = Guid.NewGuid();
    private static readonly Guid FirstMatch = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMatch = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData("goal", 6)]
    [InlineData("assist", 4)]
    [InlineData("save", 1)]
    [InlineData("penaltySave", 7)]
    [InlineData("ownGoal", -3)]
    [InlineData("penaltyMiss", -4)]
    public void EachEventIsWorthTheApprovedValue(string evento, int points)
    {
        var appearance = Appearance(Position.Midfielder) with
        {
            Goals = evento == "goal" ? 1 : 0,
            Assists = evento == "assist" ? 1 : 0,
            GoalkeeperSaves = evento == "save" ? 1 : 0,
            PenaltySaves = evento == "penaltySave" ? 1 : 0,
            OwnGoals = evento == "ownGoal" ? 1 : 0,
            PenaltyMisses = evento == "penaltyMiss" ? 1 : 0,
        };

        Assert.Equal(points, PointsOf(appearance, teamGoalsConceded: 1));
    }

    [Theory]
    [InlineData(1, 0, null, -2)]
    [InlineData(2, 1, RedCardReason.SecondYellow, -7)]
    [InlineData(0, 1, RedCardReason.Direct, -5)]
    [InlineData(1, 1, RedCardReason.Direct, -7)]
    [InlineData(2, 0, null, -7)]
    public void OnlyTheFirstYellowCountsAndTheSecondBecomesRed(
        int yellows,
        int reds,
        RedCardReason? reason,
        int points)
    {
        var appearance = Appearance(Position.Forward) with
        {
            YellowCards = yellows,
            RedCards = reds,
            RedCardReason = reason,
        };

        Assert.Equal(points, PointsOf(appearance, teamGoalsConceded: 1));
    }

    [Theory]
    [InlineData(Position.Goalkeeper, 5)]
    [InlineData(Position.Defender, 5)]
    [InlineData(Position.Midfielder, 0)]
    [InlineData(Position.Forward, 0)]
    public void CleanSheetGoesToGoalkeepersAndDefendersOnly(Position position, int points) =>
        Assert.Equal(points, PointsOf(Appearance(position), teamGoalsConceded: 0));

    [Fact]
    public void TeammateOwnGoalOrOwnRedCardTakesTheCleanSheetAway()
    {
        // O gol contra do companheiro está no placar do adversário.
        Assert.Equal(0, PointsOf(Appearance(Position.Defender), teamGoalsConceded: 1));

        var sentOff = Appearance(Position.Defender) with { RedCards = 1, RedCardReason = RedCardReason.Direct };
        Assert.Equal(-5, PointsOf(sentOff, teamGoalsConceded: 0));

        var twoYellows = Appearance(Position.Goalkeeper) with { YellowCards = 2 };
        Assert.Equal(-7, PointsOf(twoYellows, teamGoalsConceded: 0));
    }

    [Fact]
    public void BothGoalkeepersWhoPlayedWithoutConcedingGetTheBonus()
    {
        var starter = Appearance(Position.Goalkeeper) with { PlayedAsGoalkeeper = true };
        var substitute = Appearance(Position.Goalkeeper) with { PlayedAsGoalkeeper = true };

        var scores = AthleteScoring.Score(Fut7, [new(FirstMatch, 0, starter), new(FirstMatch, 0, substitute)]);

        Assert.All(scores.Values, score => Assert.Equal(5m, score.Points));
    }

    [Theory]
    [InlineData(Modality.Fut7, 2, "-2")]
    [InlineData(Modality.Futsal, 2, "-3")]
    [InlineData(Modality.Field, 2, "-4")]
    public void EachGoalConcededByTheGoalkeeperCostsTheModalityValue(
        Modality modality,
        int conceded,
        string points)
    {
        var goalkeeper = Appearance(Position.Goalkeeper) with { PlayedAsGoalkeeper = true, GoalsConceded = conceded };

        var score = AthleteScoring.Score(
            ScoringRuleSets.CurrentFor(modality), [new(FirstMatch, conceded, goalkeeper)]);

        Assert.Equal(decimal.Parse(points, CultureInfo.InvariantCulture), score.Single().Value.Points);
    }

    [Fact]
    public void RegisteredPositionDecidesEvenWhenTheAthletePlaysInGoal()
    {
        // Defensor no gol: o gol vale como de defensor, o bônus é o do defensor e os gols
        // sofridos contam para quem atuou no gol.
        var defenderInGoal = Appearance(Position.Defender) with
        {
            PlayedAsGoalkeeper = true,
            Goals = 1,
            GoalsConceded = 0,
        };
        var forwardInGoal = Appearance(Position.Forward) with
        {
            PlayedAsGoalkeeper = true,
            GoalsConceded = 0,
        };

        Assert.Equal(8 + 5, PointsOf(defenderInGoal, teamGoalsConceded: 0));
        Assert.Equal(0, PointsOf(forwardInGoal, teamGoalsConceded: 0));
    }

    [Theory]
    [InlineData(Position.Defender, 1, 0, 0, 0, 13)]
    [InlineData(Position.Defender, 1, 0, 1, 0, 8)]
    [InlineData(Position.Forward, 2, 0, 1, 0, 10)]
    [InlineData(Position.Goalkeeper, 0, 1, 4, 4, -3)]
    [InlineData(Position.Goalkeeper, 0, 6, 2, 2, 4)]
    public void ApprovedExamplesOfTheValuationTableScoreAsWritten(
        Position position,
        int goals,
        int saves,
        int teamGoalsConceded,
        int goalsConceded,
        int points)
    {
        // As colunas "Pontos" da tabela de exemplos aprovada no 01 §9 (Fut7).
        var appearance = Appearance(position) with
        {
            Goals = goals,
            GoalkeeperSaves = saves,
            PlayedAsGoalkeeper = position == Position.Goalkeeper,
            GoalsConceded = goalsConceded,
        };

        Assert.Equal(points, PointsOf(appearance, teamGoalsConceded));
    }

    [Fact]
    public void TwoMatchesInTheRoundAreScoredSeparatelyAndAdded()
    {
        var athlete = Guid.NewGuid();
        var firstMatch = Appearance(Position.Defender, athlete) with { Goals = 1 };
        var secondMatch = Appearance(Position.Defender, athlete) with { YellowCards = 1 };

        var score = AthleteScoring.Score(
            Fut7,
            [new(FirstMatch, 0, firstMatch), new(SecondMatch, 2, secondMatch)])[athlete];

        // 1º jogo: gol de defensor 8 + sem sofrer gol 5; 2º jogo: amarelo -2, sofreu gols.
        Assert.Equal(11m, score.Points);
        Assert.True(score.Played);
        Assert.Equal(
            [
                (FirstMatch, ScoringItem.Goal, 8m),
                (FirstMatch, ScoringItem.CleanSheet, 5m),
                (SecondMatch, ScoringItem.YellowCard, -2m),
            ],
            score.Lines.Select(line => (line.MatchId, line.Item, line.Points)));
    }

    [Fact]
    public void WhoDidNotPlayScoresNothingButPlayingInOneMatchCounts()
    {
        var benched = Appearance(Position.Midfielder) with { DidPlay = false };
        var athlete = Guid.NewGuid();

        var scores = AthleteScoring.Score(
            Fut7,
            [
                new(FirstMatch, 0, benched),
                new(FirstMatch, 0, Appearance(Position.Midfielder, athlete) with { DidPlay = false }),
                new(SecondMatch, 1, Appearance(Position.Midfielder, athlete)),
            ]);

        Assert.False(scores[benched.AthleteId].Played);
        Assert.Equal(0m, scores[benched.AthleteId].Points);
        Assert.Empty(scores[benched.AthleteId].Lines);
        Assert.True(scores[athlete].Played);
    }

    [Fact]
    public void CoachGetsTheAverageOfTheRoundScoresOfTeamAthletesWhoPlayed()
    {
        var otherTeam = Guid.NewGuid();
        var emptyTeam = Guid.NewGuid();
        AthleteRoundScore[] athletes =
        [
            Score(Team, played: true, points: 10m),
            Score(Team, played: true, points: 0m),
            Score(Team, played: true, points: 0m),
            Score(Team, played: false, points: 0m),
            Score(otherTeam, played: true, points: -1.5m),
            Score(otherTeam, played: true, points: 4m),
            Score(emptyTeam, played: false, points: 0m),
        ];

        var coaches = AthleteScoring.CoachPoints(athletes);

        // 10 ÷ 3 = 3,333… vira 3,33; quem não jogou não entra na conta.
        Assert.Equal(3.33m, coaches[Team]);
        Assert.Equal(1.25m, coaches[otherTeam]);
        Assert.False(coaches.ContainsKey(emptyTeam));
    }

    private static int PointsOf(MatchSheetAppearanceDefinition appearance, int teamGoalsConceded) =>
        (int)AthleteScoring.Score(Fut7, [new(FirstMatch, teamGoalsConceded, appearance)])[appearance.AthleteId].Points;

    private static AthleteRoundScore Score(Guid team, bool played, decimal points) =>
        new(Guid.NewGuid(), team, Position.Midfielder, played, points, []);

    private static MatchSheetAppearanceDefinition Appearance(Position position, Guid? athlete = null) =>
        new(
            athlete ?? Guid.NewGuid(),
            Team,
            position,
            DidPlay: true,
            PlayedAsGoalkeeper: false,
            GoalsConceded: 0,
            Goals: 0,
            Assists: 0,
            GoalkeeperSaves: 0,
            PenaltySaves: 0,
            YellowCards: 0,
            RedCards: 0,
            RedCardReason: null,
            OwnGoals: 0,
            PenaltyMisses: 0);
}
