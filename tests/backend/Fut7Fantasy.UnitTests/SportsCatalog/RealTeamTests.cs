using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.SportsCatalog;

public sealed class RealTeamTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TeamNormalizesItsNameAndKeepsTheCompetition()
    {
        var competitionId = Guid.NewGuid();
        var team = RealTeam.Create(
            Guid.NewGuid(), competitionId, new RealTeamDefinition("  Unidos da Vila  "), Now);

        Assert.Equal(competitionId, team.CompetitionId);
        Assert.Equal("Unidos da Vila", team.Name);
        Assert.False(team.IsArchived);
        Assert.Equal(Now, team.CreatedAt);
        Assert.Equal(Now, team.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public void ShortNamesAreRefused(string name)
    {
        var definition = new RealTeamDefinition(name);

        Assert.Equal(nameof(RealTeamDefinition.Name), Assert.Single(definition.Validate()).Field);
        Assert.Throws<ArgumentException>(() =>
            RealTeam.Create(Guid.NewGuid(), Guid.NewGuid(), definition, Now));
    }

    [Fact]
    public void LongNameIsRefused()
    {
        var definition = new RealTeamDefinition(new string('x', RealTeamDefinition.NameMaxLength + 1));

        Assert.Equal(nameof(RealTeamDefinition.Name), Assert.Single(definition.Validate()).Field);
    }

    [Fact]
    public void ArchivedTeamPreservesItsDataAndCannotBeChangedTwice()
    {
        var team = RealTeam.Create(
            Guid.NewGuid(), Guid.NewGuid(), new RealTeamDefinition("Estrela"), Now);
        var archivedAt = Now.AddHours(1);

        team.Archive(archivedAt);

        Assert.True(team.IsArchived);
        Assert.Equal(archivedAt, team.ArchivedAt);
        Assert.Equal("Estrela", team.Name);
        Assert.Throws<InvalidOperationException>(() =>
            team.Update(new RealTeamDefinition("Outro"), archivedAt.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => team.Archive(archivedAt.AddHours(1)));
    }
}
