using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class StageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GroupStageKeepsGroupsInOrderWithTheChosenTiebreakers()
    {
        var stage = Stage.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Groups([" Grupo A ", "Grupo B"], [TiebreakCriterion.GoalDifference, TiebreakCriterion.Wins]),
            Now);

        Assert.Equal(StageFormat.Groups, stage.Format);
        Assert.Equal(["Grupo A", "Grupo B"], stage.Groups.Select(group => group.Name));
        Assert.Equal([1, 2], stage.Groups.Select(group => group.Sequence));
        Assert.Equal([TiebreakCriterion.GoalDifference, TiebreakCriterion.Wins], stage.Tiebreakers);
    }

    [Fact]
    public void DefaultTiebreakersFollowTheUsualBrazilianOrder()
    {
        Assert.Equal(
            [
                TiebreakCriterion.Wins,
                TiebreakCriterion.GoalDifference,
                TiebreakCriterion.GoalsFor,
                TiebreakCriterion.HeadToHead,
                TiebreakCriterion.FewestRedCards,
                TiebreakCriterion.FewestYellowCards,
            ],
            StageDefinition.DefaultTiebreakers);
    }

    [Fact]
    public void KnockoutHasNoGroupsNorTiebreakers()
    {
        var stage = Stage.Create(Guid.NewGuid(), Guid.NewGuid(), 2, Knockout("Mata-mata"), Now);

        Assert.Equal(StageFormat.Knockout, stage.Format);
        Assert.Empty(stage.Groups);
        Assert.Empty(stage.Tiebreakers);
    }

    [Fact]
    public void UpdateKeepsTheIdentityOfGroupsReferencedById()
    {
        var stage = Stage.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Groups(["A", "B", "C"]), Now);
        var groupA = stage.Groups[0].Id;
        var groupC = stage.Groups[2].Id;

        stage.Update(
            new StageDefinition(
                "Primeira fase",
                StageFormat.Groups,
                [new GroupDefinition(groupC, "Grupo C"), new GroupDefinition(groupA, "Grupo A"), new(null, "Grupo D")],
                StageDefinition.DefaultTiebreakers),
            Now.AddHours(1));

        Assert.Equal("Primeira fase", stage.Name);
        Assert.Equal(["Grupo C", "Grupo A", "Grupo D"], stage.Groups.Select(group => group.Name));
        Assert.Equal(groupC, stage.Groups[0].Id);
        Assert.Equal(groupA, stage.Groups[1].Id);
        Assert.DoesNotContain(stage.Groups, group => group.Name == "B");
        Assert.Equal(Now.AddHours(1), stage.UpdatedAt);
    }

    [Fact]
    public void GroupFromAnotherStageIsRefused()
    {
        var stage = Stage.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Groups(["A"]), Now);
        var definition = new StageDefinition(
            "Grupos",
            StageFormat.Groups,
            [new GroupDefinition(Guid.NewGuid(), "Intruso")],
            StageDefinition.DefaultTiebreakers);

        Assert.False(stage.KnowsAllGroupsOf(definition));
        Assert.Throws<ArgumentException>(() => stage.Update(definition, Now));
        Assert.Equal("A", Assert.Single(stage.Groups).Name);
    }

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void InvalidDefinitionsPointToTheField(StageDefinition definition, string field)
    {
        Assert.Equal(field, Assert.Single(definition.Validate()).Field);
        Assert.Throws<ArgumentException>(() => Stage.Create(Guid.NewGuid(), Guid.NewGuid(), 1, definition, Now));
    }

    public static TheoryData<StageDefinition, string> InvalidDefinitions() => new()
    {
        { Groups(["A"]) with { Name = " x " }, nameof(StageDefinition.Name) },
        { Groups(["A"]) with { Name = new string('x', 61) }, nameof(StageDefinition.Name) },
        { Groups(["A"]) with { Format = (StageFormat)9 }, nameof(StageDefinition.Format) },
        { Groups([]), nameof(StageDefinition.Groups) },
        { Groups([.. Enumerable.Range(1, 17).Select(index => $"G{index}")]), nameof(StageDefinition.Groups) },
        { Groups(["A", " a "]), nameof(StageDefinition.Groups) },
        { Groups(["  "]), nameof(StageDefinition.Groups) },
        { Groups(["A"], []), nameof(StageDefinition.Tiebreakers) },
        { Groups(["A"], [TiebreakCriterion.Wins, TiebreakCriterion.Wins]), nameof(StageDefinition.Tiebreakers) },
        { Groups(["A"], [(TiebreakCriterion)42]), nameof(StageDefinition.Tiebreakers) },
        { Knockout("Final") with { Groups = [new(null, "A")] }, nameof(StageDefinition.Groups) },
        { Knockout("Final") with { Tiebreakers = [TiebreakCriterion.Wins] }, nameof(StageDefinition.Tiebreakers) },
    };

    [Fact]
    public void SequenceStaysWithinTheStageLimit()
    {
        var stage = Stage.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Knockout("Final"), Now);

        stage.MoveTo(StageDefinition.MaxStagesPerCompetition);
        Assert.Equal(StageDefinition.MaxStagesPerCompetition, stage.Sequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.MoveTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.MoveTo(StageDefinition.MaxStagesPerCompetition + 1));
    }

    private static StageDefinition Groups(
        IEnumerable<string> names,
        IReadOnlyList<TiebreakCriterion>? tiebreakers = null) => new(
            "Fase de grupos",
            StageFormat.Groups,
            [.. names.Select(name => new GroupDefinition(null, name))],
            tiebreakers ?? StageDefinition.DefaultTiebreakers);

    private static StageDefinition Knockout(string name) => new(name, StageFormat.Knockout, [], []);
}
