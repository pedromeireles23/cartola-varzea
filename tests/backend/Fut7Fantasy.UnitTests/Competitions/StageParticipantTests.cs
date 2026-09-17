using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class StageParticipantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParticipantKeepsStableIdentityWhenGroupChanges()
    {
        var id = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        var participant = StageParticipant.Create(id, stageId, teamId, firstGroup, Now);

        participant.AssignToGroup(secondGroup, Now.AddHours(1));

        Assert.Equal(id, participant.Id);
        Assert.Equal(stageId, participant.StageId);
        Assert.Equal(teamId, participant.RealTeamId);
        Assert.Equal(secondGroup, participant.StageGroupId);
        Assert.Equal(Now.AddHours(1), participant.UpdatedAt);
    }

    [Fact]
    public void KnockoutParticipantDoesNotNeedAGroup()
    {
        var participant = StageParticipant.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);

        Assert.Null(participant.StageGroupId);
    }

    [Fact]
    public void EmptyIdentifiersAreRefused()
    {
        Assert.Throws<ArgumentException>(() => StageParticipant.Create(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), null, Now));
        Assert.Throws<ArgumentException>(() => StageParticipant.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Now));
    }
}
