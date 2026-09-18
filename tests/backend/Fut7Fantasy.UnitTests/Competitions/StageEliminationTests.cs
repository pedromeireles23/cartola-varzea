using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class StageEliminationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    [Fact]
    public void NobodyIsEliminatedWhileOnlyOneStageHasTeams()
    {
        var groups = Stage(1);
        var knockout = Stage(2);

        Assert.Empty(StageElimination.EliminatedTeams(
            [groups, knockout],
            Participants(groups, A, B, C, D)));
    }

    [Fact]
    public void TeamsLeftOutOfTheLatestConfirmedStageAreEliminated()
    {
        var groups = Stage(1);
        var semifinal = Stage(2);
        var final = Stage(3);

        // A final ainda não tem ninguém: vale a semifinal, a fase mais adiantada confirmada.
        var eliminated = StageElimination.EliminatedTeams(
            [final, groups, semifinal],
            [.. Participants(groups, A, B, C, D), .. Participants(semifinal, A, B, C)]);

        Assert.Equal([D], eliminated);
    }

    [Fact]
    public void ConfirmingTheNextStageEliminatesWhoDidNotAdvance()
    {
        var groups = Stage(1);
        var semifinal = Stage(2);
        var final = Stage(3);

        var eliminated = StageElimination.EliminatedTeams(
            [groups, semifinal, final],
            [.. Participants(groups, A, B, C, D), .. Participants(semifinal, A, B, C), .. Participants(final, A, B)]);

        Assert.Equal(new HashSet<Guid> { C, D }, eliminated.ToHashSet());
    }

    [Fact]
    public void TeamThatNeverPlayedAStageIsNotEliminated()
    {
        // D foi cadastrado mas nunca confirmado em fase nenhuma: não jogou, então não caiu.
        var groups = Stage(1);
        var final = Stage(2);

        var eliminated = StageElimination.EliminatedTeams(
            [groups, final],
            [.. Participants(groups, A, B, C), .. Participants(final, A, B)]);

        Assert.Equal([C], eliminated);
    }

    private static Stage Stage(int sequence) => Domain.Competitions.Stage.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        sequence,
        new StageDefinition(
            $"Fase {sequence}",
            StageFormat.Knockout,
            [],
            []),
        Now);

    private static StageParticipant[] Participants(Stage stage, params Guid[] teams) =>
        [.. teams.Select(team => StageParticipant.Create(Guid.NewGuid(), stage.Id, team, null, Now))];
}
