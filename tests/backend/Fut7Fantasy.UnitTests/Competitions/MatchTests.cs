using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class MatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewMatchIsScheduledAndCountsForTheMarket()
    {
        var match = Create();

        Assert.Equal(MatchStatus.Scheduled, match.Status);
        Assert.True(match.CountsForMarket);
    }

    [Fact]
    public void TeamDoesNotPlayAgainstItself()
    {
        var team = Guid.NewGuid();
        var errors = new MatchDefinition(Guid.NewGuid(), team, team, Now).Validate();

        Assert.Contains(errors, error => error.Field == nameof(MatchDefinition.AwayTeamId));
    }

    [Fact]
    public void StageAndBothTeamsAreRequired()
    {
        var errors = new MatchDefinition(Guid.Empty, Guid.Empty, Guid.Empty, Now).Validate();

        Assert.Equal(
            [
                nameof(MatchDefinition.StageId),
                nameof(MatchDefinition.HomeTeamId),
                nameof(MatchDefinition.AwayTeamId),
            ],
            errors.Select(error => error.Field));
    }

    [Fact]
    public void PostponedMatchLeavesTheMarketButComesBackWhenRescheduled()
    {
        var match = Create();

        match.Postpone(Now);
        Assert.Equal(MatchStatus.Postponed, match.Status);
        Assert.False(match.CountsForMarket);

        var newKickoff = Now.AddDays(7);
        match.Reschedule(newKickoff, Now);
        Assert.Equal(MatchStatus.Scheduled, match.Status);
        Assert.Equal(newKickoff, match.KickoffAt);
        Assert.True(match.CountsForMarket);
    }

    [Fact]
    public void CancelledMatchIsNotRescheduled()
    {
        var match = Create();
        match.Cancel(Now);

        Assert.False(match.CountsForMarket);
        Assert.Throws<InvalidOperationException>(() => match.Reschedule(Now.AddDays(1), Now));
        Assert.Throws<InvalidOperationException>(() => match.Postpone(Now));
        Assert.Throws<InvalidOperationException>(() => match.Cancel(Now));
    }

    private static Match Create() => Match.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        new MatchDefinition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(2)),
        Now);
}

public sealed class CompetitionClockTests
{
    private const string SaoPaulo = "America/Sao_Paulo";

    [Fact]
    public void LocalTimeIsReadInTheCompetitionZoneNotInTheServerZone()
    {
        Assert.True(CompetitionClock.TryToUtc("2026-09-20T15:30", SaoPaulo, out var utc, out _));

        // São Paulo está em UTC-3 o ano todo desde que o horário de verão acabou.
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 18, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void ZoneChangesTheInstantForTheSameTextTyped()
    {
        CompetitionClock.TryToUtc("2026-09-20T15:30", SaoPaulo, out var saoPaulo, out _);
        CompetitionClock.TryToUtc("2026-09-20T15:30", "America/Rio_Branco", out var acre, out _);

        Assert.Equal(TimeSpan.FromHours(2), acre - saoPaulo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20/09/2026 15:30")]
    [InlineData("2026-09-20")]
    [InlineData("2026-13-01T10:00")]
    public void TextThatIsNotADateAndTimeIsRefused(string? value)
    {
        Assert.False(CompetitionClock.TryToUtc(value, SaoPaulo, out _, out var failure));
        Assert.Equal(LocalTimeFailure.NotATime, failure);
    }

    [Fact]
    public void UnknownZoneIsRefusedInsteadOfFallingBackToUtc()
    {
        Assert.False(CompetitionClock.TryToUtc("2026-09-20T15:30", "Mars/Olympus", out _, out var failure));
        Assert.Equal(LocalTimeFailure.UnknownTimeZone, failure);
    }

    [Fact]
    public void TimeThatDoesNotExistInTheZoneIsRefused()
    {
        // Em 2018 o Brasil ainda tinha horário de verão: 4 de novembro pulou da 0h para a 1h.
        var exists = CompetitionClock.TryToUtc("2018-11-04T00:30", SaoPaulo, out _, out var failure);

        Assert.False(exists);
        Assert.Equal(LocalTimeFailure.DoesNotExist, failure);
    }

    [Fact]
    public void TheInstantIsWrittenBackInTheCompetitionZone()
    {
        var instant = new DateTimeOffset(2026, 9, 20, 18, 30, 0, TimeSpan.Zero);

        Assert.Equal("2026-09-20T15:30", CompetitionClock.ToLocalText(instant, SaoPaulo));
    }

    [Fact]
    public void RoundTripKeepsWhatTheOrganizerTyped()
    {
        const string Typed = "2026-12-24T21:00";
        Assert.True(CompetitionClock.TryToUtc(Typed, SaoPaulo, out var utc, out _));

        Assert.Equal(Typed, CompetitionClock.ToLocalText(utc, SaoPaulo));
    }
}
