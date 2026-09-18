using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class RoundTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    [Fact]
    public void NewRoundIsADraftThatAcceptsMatches()
    {
        var round = Create();

        Assert.Equal(RoundStatus.Draft, round.Status);
        Assert.Equal(RoundPhase.Draft, round.PhaseAt(Now));
        Assert.True(round.AcceptsMatchChanges);
        Assert.Null(round.MarketCloseAt);
    }

    [Fact]
    public void OpeningTheMarketFreezesTheCloseAtOneLeadTimeBeforeTheFirstKickoff()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);

        round.OpenMarket(kickoff, OneHour, Now);

        Assert.Equal(RoundStatus.MarketOpen, round.Status);
        Assert.Equal(kickoff - OneHour, round.MarketCloseAt);
        Assert.Equal(kickoff, round.FirstKickoffAt);
        Assert.False(round.AcceptsMatchChanges);
    }

    [Fact]
    public void MarketThatWouldBeBornClosedIsRefused()
    {
        var round = Create();

        // Primeira partida daqui a 30 minutos, com antecedência de uma hora.
        var exception = Assert.Throws<InvalidOperationException>(
            () => round.OpenMarket(Now.AddMinutes(30), OneHour, Now));

        Assert.Contains("já teria fechado", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RoundStatus.Draft, round.Status);
    }

    [Fact]
    public void PhaseFollowsTheClockWithoutAnyoneChangingTheStoredStatus()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);

        Assert.Equal(RoundPhase.MarketOpen, round.PhaseAt(Now));
        Assert.Equal(RoundPhase.MarketOpen, round.PhaseAt(kickoff - OneHour - TimeSpan.FromMinutes(1)));
        Assert.Equal(RoundPhase.MarketClosed, round.PhaseAt(kickoff - OneHour));
        Assert.Equal(RoundPhase.InProgress, round.PhaseAt(kickoff));

        // O estado guardado continua o mesmo: nada de job para virar a chave.
        Assert.Equal(RoundStatus.MarketOpen, round.Status);
    }

    [Fact]
    public void ReopeningIsAllowedWhileTheMarketIsOpenAndRefusedAfterItCloses()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);

        Assert.Throws<InvalidOperationException>(() => round.ReopenForEditing(kickoff - OneHour));

        round.ReopenForEditing(Now.AddHours(1));
        Assert.Equal(RoundStatus.Draft, round.Status);
        Assert.Null(round.MarketCloseAt);
        Assert.True(round.AcceptsMatchChanges);
    }

    [Fact]
    public void ReviewStartsOnlyAfterTheFirstMatchBegins()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);

        Assert.Throws<InvalidOperationException>(() => round.BeginReview(kickoff.AddTicks(-1)));

        round.BeginReview(kickoff);
        Assert.Equal(RoundStatus.UnderReview, round.Status);
        Assert.Equal(RoundPhase.UnderReview, round.PhaseAt(kickoff));
    }

    [Fact]
    public void CancellingIsFinal()
    {
        var round = Create();
        round.Cancel(Now);

        Assert.Equal(RoundPhase.Cancelled, round.PhaseAt(Now));
        Assert.Throws<InvalidOperationException>(() => round.OpenMarket(Now.AddDays(1), OneHour, Now));
        Assert.Throws<InvalidOperationException>(() => round.Cancel(Now));
    }

    [Theory]
    [InlineData(RoundStatus.Draft, RoundStatus.MarketOpen, true)]
    [InlineData(RoundStatus.Draft, RoundStatus.Cancelled, true)]
    [InlineData(RoundStatus.Draft, RoundStatus.Published, false)]
    [InlineData(RoundStatus.Draft, RoundStatus.UnderReview, false)]
    [InlineData(RoundStatus.MarketOpen, RoundStatus.Draft, true)]
    [InlineData(RoundStatus.MarketOpen, RoundStatus.UnderReview, true)]
    [InlineData(RoundStatus.MarketOpen, RoundStatus.Published, false)]
    [InlineData(RoundStatus.UnderReview, RoundStatus.Published, true)]
    [InlineData(RoundStatus.UnderReview, RoundStatus.Draft, false)]
    [InlineData(RoundStatus.Published, RoundStatus.UnderReview, true)]
    [InlineData(RoundStatus.Cancelled, RoundStatus.Draft, false)]
    [InlineData(RoundStatus.Cancelled, RoundStatus.MarketOpen, false)]
    public void LifecycleAllowsOnlyTheDocumentedTransitions(
        RoundStatus from,
        RoundStatus to,
        bool allowed)
    {
        Assert.Equal(allowed, RoundLifecycle.Allows(from, to));
    }

    [Fact]
    public void NameIsRequiredAndBounded()
    {
        Assert.NotEmpty(new RoundDefinition(" ").Validate());
        Assert.NotEmpty(new RoundDefinition("a").Validate());
        Assert.NotEmpty(new RoundDefinition(new string('a', RoundDefinition.NameMaxLength + 1)).Validate());
        Assert.Empty(new RoundDefinition("  Rodada 1  ").Validate());
        Assert.Equal("Rodada 1", new RoundDefinition("  Rodada 1  ").Normalized().Name);
    }

    [Fact]
    public void SequenceStaysInsideTheLimit()
    {
        var round = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => round.MoveTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => round.MoveTo(Round.MaxRoundsPerCompetition + 1));
        round.MoveTo(Round.MaxRoundsPerCompetition);
        Assert.Equal(Round.MaxRoundsPerCompetition, round.Sequence);
    }

    private static Round Create() =>
        Round.Create(Guid.NewGuid(), Guid.NewGuid(), 1, new RoundDefinition("Rodada 1"), Now);
}
