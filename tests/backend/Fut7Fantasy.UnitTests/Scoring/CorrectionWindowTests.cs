using Fut7Fantasy.Domain.Scoring;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>Janela de correção em dias úteis do fuso do campeonato (01 §9, 03 §7).</summary>
public sealed class CorrectionWindowTests
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    [Fact]
    public void ThreeBusinessDaysAfterASundayCloseEndOnWednesdayAtTheSameHour()
    {
        var closedSunday = new DateTimeOffset(2026, 9, 20, 10, 0, 0, Brasilia);
        var publishedMonday = new DateTimeOffset(2026, 9, 21, 9, 0, 0, Brasilia);

        var consolidatesAt = CorrectionWindow.ConsolidatesAt(closedSunday, publishedMonday, 3, "America/Sao_Paulo");

        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 0, 0, Brasilia), consolidatesAt);
    }

    [Fact]
    public void PublishingOnTheDeadlineStillLeavesOneBusinessDayForCorrections()
    {
        var closedSunday = new DateTimeOffset(2026, 9, 20, 10, 0, 0, Brasilia);
        var publishedWednesday = new DateTimeOffset(2026, 9, 23, 9, 30, 0, Brasilia);

        var consolidatesAt = CorrectionWindow.ConsolidatesAt(
            closedSunday, publishedWednesday, 3, "America/Sao_Paulo");

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 9, 30, 0, Brasilia), consolidatesAt);
    }

    [Fact]
    public void WeekendDoesNotCountAsBusinessDay()
    {
        var closedFriday = new DateTimeOffset(2026, 9, 25, 20, 0, 0, Brasilia);

        var consolidatesAt = CorrectionWindow.ConsolidatesAt(closedFriday, closedFriday, 1, "America/Sao_Paulo");

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 20, 0, 0, Brasilia), consolidatesAt);
    }

    [Fact]
    public void BusinessDaysFollowTheCompetitionTimeZoneNotUtc()
    {
        // Sexta às 22h em Manaus já é sábado em UTC; o dia útil é o do campeonato.
        var manaus = TimeSpan.FromHours(-4);
        var closedFriday = new DateTimeOffset(2026, 9, 25, 22, 0, 0, manaus);

        var consolidatesAt = CorrectionWindow.ConsolidatesAt(closedFriday, closedFriday, 1, "America/Manaus");

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 22, 0, 0, manaus), consolidatesAt);
    }
}
