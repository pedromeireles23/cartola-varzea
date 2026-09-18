using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class RegistrationWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Home = Guid.NewGuid();
    private static readonly Guid Away = Guid.NewGuid();

    [Fact]
    public void ConfiguredDeadlineWinsAndIsReadInTheCompetitionTimeZone()
    {
        var competition = Draft("2026-10-04T18:00");

        var window = RegistrationWindow.For(competition, [], [], []);

        // 18h em Brasília é 21h em UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 21, 0, 0, TimeSpan.Zero), window.ClosesAt);
        Assert.Equal(RegistrationDeadlineSource.Configured, window.Source);
    }

    [Fact]
    public void WithoutAFirstStageRoundRegistrationStaysOpen()
    {
        var groups = Stage(1);

        var window = RegistrationWindow.For(Draft(), [groups], [], []);

        Assert.Equal(RegistrationDeadlineSource.NotYetDefined, window.Source);
        Assert.True(window.IsOpenAt(Now.AddYears(1)));
    }

    [Fact]
    public void DefaultIsTheMarketCloseOfTheLastRoundOfTheFirstStage()
    {
        var competition = Draft();
        var groups = Stage(1);
        var final = Stage(2);
        var first = Round(1, "Rodada 1");
        var second = Round(2, "Rodada 2");
        var decider = Round(3, "Final");
        var kickoff = Now.AddDays(3);

        // A rodada 2 é a última com jogo da primeira fase; a final é de outra fase.
        List<Match> matches =
        [
            Match(first, groups, Now.AddDays(1)),
            Match(second, groups, kickoff),
            Match(decider, final, Now.AddDays(10)),
        ];

        var beforeOpening = RegistrationWindow.For(competition, [final, groups], [first, second, decider], matches);
        Assert.Equal(RegistrationDeadlineSource.FirstStageLastRound, beforeOpening.Source);
        Assert.Equal("Rodada 2", beforeOpening.RoundName);

        // Antes de a rodada abrir o mercado o fechamento dela não existe: inscrição aberta.
        Assert.Null(beforeOpening.ClosesAt);

        second.OpenMarket(kickoff, TimeSpan.FromHours(1), Now);
        var afterOpening = RegistrationWindow.For(competition, [final, groups], [first, second, decider], matches);

        Assert.Equal(kickoff.AddHours(-1), afterOpening.ClosesAt);
        Assert.True(afterOpening.IsOpenAt(kickoff.AddHours(-1).AddTicks(-1)));
        Assert.False(afterOpening.IsOpenAt(kickoff.AddHours(-1)));
    }

    [Fact]
    public void CancelledMatchDoesNotHoldTheDeadline()
    {
        var groups = Stage(1);
        var first = Round(1, "Rodada 1");
        var second = Round(2, "Rodada 2");
        var cancelled = Match(second, groups, Now.AddDays(3));
        cancelled.Cancel(Now);

        var window = RegistrationWindow.For(
            Draft(), [groups], [first, second], [Match(first, groups, Now.AddDays(1)), cancelled]);

        Assert.Equal("Rodada 1", window.RoundName);
    }

    [Fact]
    public void DeadlineThatIsNotADateIsRefused()
    {
        var errors = (Settings() with { RegistrationDeadlineLocal = "amanhã" }).Validate();

        Assert.Equal(nameof(CompetitionSettings.RegistrationDeadlineLocal), Assert.Single(errors).Field);
    }

    private static CompetitionSettings Settings(string? deadline = null) => new(
        "Copa do Prazo",
        "2026",
        Modality.Fut7,
        CompetitionSettings.DefaultTimeZoneId,
        TimeSpan.Zero,
        CompetitionSettings.DefaultResultsSlaBusinessDays,
        CompetitionSettings.DefaultCorrectionWindowBusinessDays,
        deadline);

    private static Competition Draft(string? deadline = null) =>
        Competition.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Settings(deadline), Now);

    private static Stage Stage(int sequence) => Domain.Competitions.Stage.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        sequence,
        new StageDefinition($"Fase {sequence}", StageFormat.Knockout, [], []),
        Now);

    private static Round Round(int sequence, string name) =>
        Domain.Competitions.Round.Create(Guid.NewGuid(), Guid.NewGuid(), sequence, new RoundDefinition(name), Now);

    private static Match Match(Round round, Stage stage, DateTimeOffset kickoff) =>
        Domain.Competitions.Match.Create(
            Guid.NewGuid(),
            round.CompetitionId,
            round.Id,
            new MatchDefinition(stage.Id, Home, Away, kickoff),
            Now);
}
