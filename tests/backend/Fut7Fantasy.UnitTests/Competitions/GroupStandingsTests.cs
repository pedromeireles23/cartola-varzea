using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

/// <summary>
/// A tabela de um grupo de pontos corridos (01 §7).
///
/// Vitória 3, empate 1, derrota 0; depois disso manda a ordem de critérios que a
/// organização escolheu, porque a várzea varia e quem decide é quem organiza.
/// </summary>
public sealed class GroupStandingsTests
{
    private static readonly Guid Alfa = new("11111111-0000-0000-0000-000000000000");
    private static readonly Guid Beta = new("22222222-0000-0000-0000-000000000000");
    private static readonly Guid Gama = new("33333333-0000-0000-0000-000000000000");

    [Fact]
    public void RecordCountsPointsGoalsAndResults()
    {
        var records = GroupStandings.Build(
            [Alfa, Beta],
            [new(Alfa, 3, Beta, 1), new(Beta, 2, Alfa, 2)]);

        var alfa = records.Single(record => record.TeamId == Alfa);
        Assert.Equal(2, alfa.Played);
        Assert.Equal(1, alfa.Wins);
        Assert.Equal(1, alfa.Draws);
        Assert.Equal(0, alfa.Losses);
        Assert.Equal(5, alfa.GoalsFor);
        Assert.Equal(3, alfa.GoalsAgainst);
        Assert.Equal(2, alfa.GoalDifference);
        Assert.Equal(4, alfa.Points);

        var beta = records.Single(record => record.TeamId == Beta);
        Assert.Equal(0, beta.Wins);
        Assert.Equal(1, beta.Draws);
        Assert.Equal(1, beta.Losses);
        Assert.Equal(1, beta.Points);
    }

    [Fact]
    public void TeamWithoutMatchesEntersWithZeros()
    {
        var records = GroupStandings.Build([Alfa, Beta], []);

        Assert.All(records, record => Assert.Equal(0, record.Played));
        Assert.All(records, record => Assert.Equal(0, record.Points));
        // Estar na fase e ainda não ter jogado é diferente de não estar nela.
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void PointsDecideBeforeAnyTiebreaker()
    {
        // Alfa ganhou os dois por 1 a 0; Beta goleou um e perdeu o outro. Beta tem saldo
        // muito melhor e menos pontos: saldo só entra depois dos pontos.
        var jogos = new StandingsMatch[]
        {
            new(Alfa, 1, Gama, 0),
            new(Alfa, 1, Beta, 0),
            new(Beta, 9, Gama, 0),
        };
        var rows = GroupStandings.Order(
            GroupStandings.Build([Alfa, Beta], jogos),
            jogos,
            [TiebreakCriterion.GoalDifference]);

        Assert.Equal(Alfa, rows[0].Record.TeamId);
        Assert.Equal(6, rows[0].Record.Points);
        Assert.Equal(2, rows[0].Record.GoalDifference);
        Assert.Equal(Beta, rows[1].Record.TeamId);
        Assert.Equal(3, rows[1].Record.Points);
        Assert.Equal(8, rows[1].Record.GoalDifference);
    }

    [Fact]
    public void OrganizerChoosesWhichCriterionDecides()
    {
        // Empatados em pontos: Alfa tem mais saldo, Beta tem mais gols pró.
        var jogos = new StandingsMatch[]
        {
            new(Alfa, 2, Gama, 0),
            new(Beta, 5, Gama, 4),
        };
        var records = GroupStandings.Build([Alfa, Beta], jogos);

        var porSaldo = GroupStandings.Order(records, jogos, [TiebreakCriterion.GoalDifference]);
        Assert.Equal(Alfa, porSaldo[0].Record.TeamId);

        var porGols = GroupStandings.Order(records, jogos, [TiebreakCriterion.GoalsFor]);
        Assert.Equal(Beta, porGols[0].Record.TeamId);
    }

    [Fact]
    public void HeadToHeadUsesTheMatchBetweenTheTiedTeams()
    {
        var jogos = new StandingsMatch[]
        {
            // Mesma pontuação e mesmo saldo; quem ganhou o confronto foi Beta.
            new(Beta, 1, Alfa, 0),
            new(Alfa, 3, Gama, 2),
            new(Gama, 2, Beta, 3),
        };
        var records = GroupStandings.Build([Alfa, Beta], jogos);

        var rows = GroupStandings.Order(records, jogos, [TiebreakCriterion.HeadToHead]);

        Assert.Equal(Beta, rows[0].Record.TeamId);
        Assert.Equal(Alfa, rows[1].Record.TeamId);
        Assert.False(rows[0].Tied);
    }

    [Fact]
    public void WithoutAMatchBetweenThemHeadToHeadDecidesNothing()
    {
        var jogos = new StandingsMatch[] { new(Alfa, 1, Gama, 0), new(Beta, 1, Gama, 0) };
        var records = GroupStandings.Build([Alfa, Beta], jogos);

        var rows = GroupStandings.Order(records, jogos, [TiebreakCriterion.HeadToHead]);

        // Ninguém decidiu: os dois dividem a colocação.
        Assert.Equal(1, rows[0].Position);
        Assert.Equal(1, rows[1].Position);
        Assert.All(rows, row => Assert.True(row.Tied));
    }

    [Fact]
    public void ExhaustedCriteriaLeaveTheTieStanding()
    {
        var jogos = new StandingsMatch[] { new(Alfa, 1, Gama, 0), new(Beta, 1, Gama, 0) };
        var rows = GroupStandings.Order(
            GroupStandings.Build([Alfa, Beta], jogos),
            jogos,
            [TiebreakCriterion.GoalDifference, TiebreakCriterion.GoalsFor]);

        Assert.Equal([1, 1], rows.Select(row => row.Position));
        Assert.All(rows, row => Assert.True(row.Tied));
    }

    [Fact]
    public void SharedPlaceSkipsTheNextPosition()
    {
        // Dois empatados em primeiro e um atrás: 1º, 1º, 3º.
        var jogos = new StandingsMatch[] { new(Alfa, 1, Gama, 0), new(Beta, 1, Gama, 0) };
        var rows = GroupStandings.Order(
            GroupStandings.Build([Alfa, Beta, Gama], jogos),
            jogos,
            [TiebreakCriterion.GoalDifference]);

        Assert.Equal([1, 1, 3], rows.Select(row => row.Position));
        Assert.False(rows[2].Tied);
    }

    [Fact]
    public void WithoutCriteriaOnlyPointsDecideAndTheRestTies()
    {
        var jogos = new StandingsMatch[] { new(Alfa, 5, Gama, 0), new(Beta, 1, Gama, 0) };
        var rows = GroupStandings.Order(GroupStandings.Build([Alfa, Beta], jogos), jogos, []);

        // Mata-mata não configura critério; sem eles, saldo não vale nada.
        Assert.Equal([1, 1], rows.Select(row => row.Position));
        Assert.All(rows, row => Assert.True(row.Tied));
    }

    [Fact]
    public void CircularHeadToHeadDoesNotThrowAndStaysDeterministic()
    {
        // A ganha de B, B de C e C de A: o confronto direto não é transitivo, e uma
        // ordenação que valide consistência quebraria aqui.
        var jogos = new StandingsMatch[]
        {
            new(Alfa, 1, Beta, 0),
            new(Beta, 1, Gama, 0),
            new(Gama, 1, Alfa, 0),
        };
        var records = GroupStandings.Build([Alfa, Beta, Gama], jogos);

        var primeira = GroupStandings.Order(records, jogos, [TiebreakCriterion.HeadToHead]);
        var segunda = GroupStandings.Order(records.Reverse(), jogos, [TiebreakCriterion.HeadToHead]);

        Assert.Equal(
            primeira.Select(row => row.Record.TeamId),
            segunda.Select(row => row.Record.TeamId));
    }

    [Fact]
    public void TheSameInputAlwaysGivesTheSameOrder()
    {
        var jogos = new StandingsMatch[] { new(Alfa, 2, Beta, 1), new(Gama, 0, Alfa, 0) };
        var records = GroupStandings.Build([Alfa, Beta, Gama], jogos);
        var criterios = new[] { TiebreakCriterion.GoalDifference, TiebreakCriterion.GoalsFor };

        var esperada = GroupStandings.Order(records, jogos, criterios)
            .Select(row => row.Record.TeamId)
            .ToList();

        // Qualquer ordem de entrada produz a mesma tabela: nada depende de sorteio.
        Assert.Equal(
            esperada,
            GroupStandings.Order(records.Reverse(), jogos, criterios)
                .Select(row => row.Record.TeamId));
    }
}
