using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class MatchSheetTests
{
    private static readonly Guid Home = Guid.NewGuid();
    private static readonly Guid Away = Guid.NewGuid();

    [Fact]
    public void ValidatesASheetWhoseEventsAndGoalkeepersExplainTheScore()
    {
        var sheet = new MatchSheetDefinition(2, 1,
        [
            Appearance(Home, goals: 2),
            Appearance(Home, goalkeeper: true, goalsConceded: 1),
            Appearance(Away, goals: 1),
            Appearance(Away, goalkeeper: true, goalsConceded: 2),
        ]);

        Assert.Empty(sheet.Validate(Home, Away));
    }

    [Fact]
    public void RefusesStatisticsForAnAthleteWhoDidNotPlay()
    {
        var absent = Appearance(Home, didPlay: false, goals: 1);
        var sheet = new MatchSheetDefinition(1, 0,
        [
            absent,
            Appearance(Home, goalkeeper: true),
            Appearance(Away, goalkeeper: true, goalsConceded: 1),
        ]);

        Assert.Contains(sheet.Validate(Home, Away), error => error.Field == "DidPlay");
    }

    [Fact]
    public void RefusesARecordedScoreThatEventsDoNotExplain()
    {
        var sheet = new MatchSheetDefinition(2, 0,
        [
            Appearance(Home, goals: 1),
            Appearance(Home, goalkeeper: true),
            Appearance(Away, goalkeeper: true, goalsConceded: 2),
        ]);

        Assert.Contains(sheet.Validate(Home, Away), error => error.Message.Contains("corresponder ao placar"));
    }

    [Fact]
    public void SecondYellowRequiresTwoYellowCards()
    {
        var sentOff = Appearance(Home, yellowCards: 1, redCards: 1, reason: RedCardReason.SecondYellow);
        var sheet = new MatchSheetDefinition(0, 0,
        [
            sentOff,
            Appearance(Home, goalkeeper: true),
            Appearance(Away, goalkeeper: true),
        ]);

        Assert.Contains(sheet.Validate(Home, Away), error => error.Field == "YellowCards");
    }

    private static MatchSheetAppearanceDefinition Appearance(
        Guid teamId,
        bool didPlay = true,
        bool goalkeeper = false,
        int goalsConceded = 0,
        int goals = 0,
        int yellowCards = 0,
        int redCards = 0,
        RedCardReason? reason = null) =>
        new(
            Guid.NewGuid(),
            teamId,
            goalkeeper ? Position.Goalkeeper : Position.Forward,
            didPlay,
            goalkeeper,
            goalsConceded,
            goals,
            0,
            0,
            0,
            yellowCards,
            redCards,
            reason,
            0,
            0);
}
