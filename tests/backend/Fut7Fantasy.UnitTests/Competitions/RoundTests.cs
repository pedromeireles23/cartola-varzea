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

    [Fact]
    public void PublishingFromReviewIsProvisionalUntilTheClockReachesTheConsolidation()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);
        Assert.Throws<InvalidOperationException>(() => round.Publish(kickoff, kickoff.AddDays(3)));

        round.BeginReview(kickoff);
        var publishedAt = kickoff.AddDays(1);
        var consolidatesAt = kickoff.AddDays(3);
        round.Publish(publishedAt, consolidatesAt);

        Assert.Equal(RoundStatus.Published, round.Status);
        Assert.Equal(publishedAt, round.PublishedAt);
        Assert.Equal(consolidatesAt, round.ConsolidatesAt);
        Assert.Equal(RoundPhase.Published, round.PhaseAt(consolidatesAt.AddTicks(-1)));
        Assert.Equal(RoundPhase.Consolidated, round.PhaseAt(consolidatesAt));
    }

    [Fact]
    public void ConsolidationBeforeTheMarketClosedIsRefused()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);
        round.BeginReview(kickoff);

        Assert.Throws<InvalidOperationException>(() => round.Publish(kickoff, round.MarketCloseAt!.Value));
    }

    [Fact]
    public void ReopeningAProvisionalRoundDoesNotRequireAReason()
    {
        var round = Published(out var consolidatesAt);
        var stillProvisional = consolidatesAt.AddTicks(-1);

        round.ReopenForCorrection(stillProvisional, reason: null);

        Assert.Equal(RoundStatus.UnderReview, round.Status);
        Assert.Equal(RoundPhase.ReopenedForCorrection, round.PhaseAt(stillProvisional));
        Assert.True(round.IsUnderCorrection);
        Assert.Equal(stillProvisional, round.ReopenedAt);
        Assert.Null(round.CorrectionReason);

        // A apuração vigente não é desfeita: a rodada continua sabendo quando saiu.
        Assert.NotNull(round.PublishedAt);
        Assert.Equal(consolidatesAt, round.ConsolidatesAt);
    }

    [Fact]
    public void ReopeningAConsolidatedRoundWithoutAReasonIsRefused()
    {
        var round = Published(out var consolidatesAt);

        var exception = Assert.Throws<InvalidOperationException>(
            () => round.ReopenForCorrection(consolidatesAt, reason: "   "));

        Assert.Contains("já consolidou", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RoundStatus.Published, round.Status);
        Assert.False(round.IsUnderCorrection);
    }

    [Fact]
    public void ReasonThatExplainsNothingIsRefused()
    {
        var round = Published(out var consolidatesAt);

        Assert.Throws<InvalidOperationException>(() => round.ReopenForCorrection(consolidatesAt, "errado"));
        Assert.Throws<InvalidOperationException>(
            () => round.ReopenForCorrection(consolidatesAt, new string('a', Round.CorrectionReasonMaxLength + 1)));
        Assert.Equal(RoundStatus.Published, round.Status);
    }

    [Fact]
    public void RepublishingClearsTheReopeningAndStartsANewProvisionalWindow()
    {
        var round = Published(out var consolidatesAt);
        round.ReopenForCorrection(consolidatesAt, "Gol lançado no atleta errado.");

        var republishedAt = consolidatesAt.AddDays(1);
        var newWindow = republishedAt.AddDays(1);
        round.Publish(republishedAt, newWindow);

        Assert.Equal(RoundStatus.Published, round.Status);
        Assert.False(round.IsUnderCorrection);
        Assert.Null(round.ReopenedAt);
        Assert.Null(round.CorrectionReason);
        Assert.Equal(republishedAt, round.PublishedAt);
        Assert.Equal(RoundPhase.Published, round.PhaseAt(republishedAt));
    }

    [Fact]
    public void OnlyAPublishedRoundIsReopenedForCorrection()
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        Assert.Throws<InvalidOperationException>(() => round.ReopenForCorrection(Now, "Motivo suficiente."));

        round.OpenMarket(kickoff, OneHour, Now);
        round.BeginReview(kickoff);

        // Em conferência pela primeira vez não é correção: não há resultado para corrigir.
        Assert.Equal(RoundPhase.UnderReview, round.PhaseAt(kickoff));
        Assert.False(round.IsUnderCorrection);
        Assert.Throws<InvalidOperationException>(() => round.ReopenForCorrection(kickoff, "Motivo suficiente."));
    }

    private static Round Published(out DateTimeOffset consolidatesAt)
    {
        var round = Create();
        var kickoff = Now.AddDays(2);
        round.OpenMarket(kickoff, OneHour, Now);
        round.BeginReview(kickoff);
        consolidatesAt = kickoff.AddDays(3);
        round.Publish(kickoff.AddDays(1), consolidatesAt);
        return round;
    }

    private static Round Create() =>
        Round.Create(Guid.NewGuid(), Guid.NewGuid(), 1, new RoundDefinition("Rodada 1"), Now);
}
