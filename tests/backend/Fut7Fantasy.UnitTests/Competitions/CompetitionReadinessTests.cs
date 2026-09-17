using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Competitions;

/// <summary>
/// Checklist de publicação (01 §7 e §9). Os cenários usam Fut7, cuja formação titular é
/// 1 GOL, 2 DEF, 2 MEI e 2 ATA, com um reserva por posição.
/// </summary>
public sealed class CompetitionReadinessTests
{
    private static readonly ModalityProfile Fut7 = ModalityProfiles.CurrentFor(Modality.Fut7);

    [Fact]
    public void CompetitionWithCatalogAndConfirmedTeamsIsReadyToPublish()
    {
        var report = CompetitionReadiness.Evaluate(Fut7, Ready());

        Assert.True(report.CanPublish);
        Assert.Empty(report.Items);
    }

    [Fact]
    public void EmptyDraftListsEveryBlocker()
    {
        var report = CompetitionReadiness.Evaluate(
            Fut7,
            new CompetitionReadinessSnapshot([], [], new Dictionary<Position, int>(), false));

        Assert.False(report.CanPublish);
        Assert.Contains(CompetitionReadiness.NoStagesCode, Codes(report));
        Assert.Contains(CompetitionReadiness.NotEnoughTeamsCode, Codes(report));

        // Uma linha por posição faltante, para o organizador saber quem contratar.
        Assert.Equal(
            Enum.GetValues<Position>().Length,
            Codes(report).Count(code => code == CompetitionReadiness.NotEnoughAthletesCode));
    }

    [Theory]
    [InlineData(Position.Goalkeeper, 2)]
    [InlineData(Position.Defender, 3)]
    [InlineData(Position.Midfielder, 3)]
    [InlineData(Position.Forward, 3)]
    public void EachPositionNeedsTheStartersPlusOneReserve(Position position, int required)
    {
        var enough = Ready() with { AvailableAthletesByPosition = Squad(position, required) };
        var missing = Ready() with { AvailableAthletesByPosition = Squad(position, required - 1) };

        Assert.DoesNotContain(
            CompetitionReadiness.NotEnoughAthletesCode,
            Codes(CompetitionReadiness.Evaluate(Fut7, enough)));

        var report = CompetitionReadiness.Evaluate(Fut7, missing);
        Assert.False(report.CanPublish);
        Assert.Contains(CompetitionReadiness.NotEnoughAthletesCode, Codes(report));
    }

    [Fact]
    public void PublishingNeedsAtLeastTwoActiveTeams()
    {
        var single = Ready() with { ActiveTeams = [new("Alpha", 9)] };

        var report = CompetitionReadiness.Evaluate(Fut7, single);

        Assert.False(report.CanPublish);
        Assert.Contains(CompetitionReadiness.NotEnoughTeamsCode, Codes(report));
    }

    [Fact]
    public void EmptyStagesBlockOnlyWhenNoStageHasTeams()
    {
        var nobody = Ready() with
        {
            Stages = [new("Fase de grupos", 0), new("Semifinal", 0)],
        };
        var blocked = CompetitionReadiness.Evaluate(Fut7, nobody);
        Assert.False(blocked.CanPublish);
        Assert.Contains(CompetitionReadiness.NoStageParticipantsCode, Codes(blocked));
        Assert.DoesNotContain(CompetitionReadiness.StageWithoutParticipantsCode, Codes(blocked));

        // Fase seguinte vazia é o normal: os classificados só são confirmados depois.
        var partial = Ready() with
        {
            Stages = [new("Fase de grupos", 4), new("Semifinal", 0)],
        };
        var report = CompetitionReadiness.Evaluate(Fut7, partial);
        Assert.True(report.CanPublish);
        Assert.Contains(CompetitionReadiness.StageWithoutParticipantsCode, Codes(report));
        Assert.Contains("Semifinal", Messages(report, CompetitionReadiness.StageWithoutParticipantsCode));
    }

    [Fact]
    public void ThinRealTeamRosterWarnsWithoutBlocking()
    {
        var thin = Ready() with
        {
            ActiveTeams = [new("Alpha", Fut7.MinimumAthletesPerRealTeam - 1), new("Beta", 11)],
        };

        var report = CompetitionReadiness.Evaluate(Fut7, thin);

        Assert.True(report.CanPublish);
        Assert.Contains(CompetitionReadiness.ThinRealTeamRosterCode, Codes(report));
        Assert.Contains("Alpha", Messages(report, CompetitionReadiness.ThinRealTeamRosterCode));
        Assert.DoesNotContain("Beta", Messages(report, CompetitionReadiness.ThinRealTeamRosterCode));
    }

    [Fact]
    public void SinglePriceLevelWarnsBecauseTheBudgetStopsForcingChoices()
    {
        var report = CompetitionReadiness.Evaluate(Fut7, Ready() with { AssetsShareOnePriceLevel = true });

        Assert.True(report.CanPublish);
        Assert.Contains(CompetitionReadiness.SinglePriceLevelCode, Codes(report));
    }

    [Fact]
    public void BlockersComeBeforeWarnings()
    {
        var mixed = Ready() with
        {
            ActiveTeams = [new("Alpha", 1)],
            AssetsShareOnePriceLevel = true,
        };

        var items = CompetitionReadiness.Evaluate(Fut7, mixed).Items;

        Assert.Equal(
            [.. items.OrderByDescending(item => item.Severity)],
            items);
    }

    /// <summary>Catálogo mínimo que passa no checklist, usado como base dos cenários.</summary>
    private static CompetitionReadinessSnapshot Ready() => new(
        [new("Fase de grupos", 2)],
        [new("Alpha", 11), new("Beta", 11)],
        Squad(),
        AssetsShareOnePriceLevel: false);

    private static Dictionary<Position, int> Squad() =>
        Enum.GetValues<Position>().ToDictionary(
            position => position,
            position => Fut7.Formation.CountOf(position) + ModalityProfile.BenchPerPosition);

    private static Dictionary<Position, int> Squad(Position position, int count)
    {
        var squad = Squad();
        squad[position] = count;
        return squad;
    }

    private static List<string> Codes(CompetitionReadinessReport report) =>
        [.. report.Items.Select(item => item.Code)];

    private static string Messages(CompetitionReadinessReport report, string code) =>
        string.Join(" | ", report.Items.Where(item => item.Code == code).Select(item => item.Message));
}
