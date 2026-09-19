using System.Globalization;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>A tabela "Pontuação v1" do 01 §9, linha por linha.</summary>
public sealed class ScoringRuleSetTests
{
    [Theory]
    [InlineData(Modality.Fut7, Position.Goalkeeper, 10)]
    [InlineData(Modality.Fut7, Position.Defender, 8)]
    [InlineData(Modality.Fut7, Position.Midfielder, 6)]
    [InlineData(Modality.Fut7, Position.Forward, 5)]
    [InlineData(Modality.Futsal, Position.Goalkeeper, 9)]
    [InlineData(Modality.Futsal, Position.Defender, 9)]
    [InlineData(Modality.Futsal, Position.Midfielder, 5)]
    [InlineData(Modality.Futsal, Position.Forward, 4)]
    [InlineData(Modality.Field, Position.Goalkeeper, 13)]
    [InlineData(Modality.Field, Position.Defender, 10)]
    [InlineData(Modality.Field, Position.Midfielder, 8)]
    [InlineData(Modality.Field, Position.Forward, 7)]
    public void GoalIsWorthWhatThePositionAndModalitySay(Modality modality, Position position, int points) =>
        Assert.Equal(points, ScoringRuleSets.CurrentFor(modality).GoalFor(position));

    [Theory]
    [InlineData(Modality.Fut7, "4", "5", "-1")]
    [InlineData(Modality.Futsal, "3", "8", "-1.5")]
    [InlineData(Modality.Field, "5", "3", "-2")]
    public void AssistCleanSheetAndGoalConcededAreCalibratedPerModality(
        Modality modality,
        string assist,
        string cleanSheet,
        string goalConceded)
    {
        var rules = ScoringRuleSets.CurrentFor(modality);

        Assert.Equal(D(assist), rules.Assist);
        Assert.Equal(D(cleanSheet), rules.CleanSheet);
        Assert.Equal(D(goalConceded), rules.GoalConceded);
    }

    [Theory]
    [InlineData(Modality.Fut7)]
    [InlineData(Modality.Futsal)]
    [InlineData(Modality.Field)]
    public void OtherEventsAreTheSameInEveryModality(Modality modality)
    {
        var rules = ScoringRuleSets.CurrentFor(modality);

        Assert.Equal(7m, rules.PenaltySave);
        Assert.Equal(1m, rules.GoalkeeperSave);
        Assert.Equal(-2m, rules.YellowCard);
        Assert.Equal(-5m, rules.RedCard);
        Assert.Equal(-3m, rules.OwnGoal);
        Assert.Equal(-4m, rules.PenaltyMiss);
        Assert.Equal(2m, rules.CaptainMultiplier);
    }

    [Fact]
    public void EveryModalityHasAVersionedRuleSet()
    {
        Assert.Equal(Enum.GetValues<Modality>(), ScoringRuleSets.Current.Select(rules => rules.Modality));
        Assert.All(ScoringRuleSets.Current, rules => Assert.Equal(1, rules.Version));
        Assert.Same(ScoringRuleSets.CurrentFor(Modality.Futsal), ScoringRuleSets.Find(Modality.Futsal, 1));
        Assert.Null(ScoringRuleSets.Find(Modality.Futsal, 2));
    }

    [Theory]
    [InlineData("3.333333", "3.33")]
    [InlineData("0.666666", "0.67")]
    [InlineData("-0.666666", "-0.67")]
    [InlineData("0.125", "0.13")]
    [InlineData("-0.125", "-0.13")]
    [InlineData("1.5", "1.5")]
    public void DerivedValuesKeepTwoDecimalsRoundingHalfAwayFromZero(string value, string expected) =>
        Assert.Equal(D(expected), ScoringRuleSet.Round(D(value)));

    /// <summary>Decimal escrito com ponto: atributos não aceitam literais `decimal`.</summary>
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
