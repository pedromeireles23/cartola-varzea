using Fut7Fantasy.Domain.Scoring;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>
/// A ordem do ranking (01 §9): pontos totais, patrimônio, pontos da rodada mais recente
/// e, dentro do empate, a entrada mais antiga. Todos os casos são determinísticos: é o
/// que o critério de saída da Fase 11 exige.
/// </summary>
public sealed class RankingTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TotalPointsDecideBeforeAnythingElse()
    {
        var ranked = Ranking.Order([
            Entry("b", points: 40, netWorth: 200, lastRound: 30),
            Entry("a", points: 41, netWorth: 100, lastRound: 1),
        ]);

        Assert.Equal(["a", "b"], Names(ranked));
        Assert.Equal([1, 2], ranked.Select(item => item.Position));
        Assert.All(ranked, item => Assert.False(item.Tied));
    }

    [Fact]
    public void NetWorthBreaksEqualPointsAndTheLastRoundBreaksEqualNetWorth()
    {
        var ranked = Ranking.Order([
            Entry("c", points: 40, netWorth: 100, lastRound: 5),
            Entry("a", points: 40, netWorth: 110, lastRound: 0),
            Entry("b", points: 40, netWorth: 100, lastRound: 9),
        ]);

        Assert.Equal(["a", "b", "c"], Names(ranked));
    }

    [Fact]
    public void WhoDidNotPlayTheLastRoundLosesEvenToANegativeScore()
    {
        var ranked = Ranking.Order([
            Entry("ausente", points: 40, netWorth: 100, lastRound: null),
            Entry("jogou mal", points: 40, netWorth: 100, lastRound: -8),
        ]);

        Assert.Equal(["jogou mal", "ausente"], Names(ranked));
    }

    [Fact]
    public void IdenticalNumbersShareThePositionAndTheNextOneSkipsIt()
    {
        var ranked = Ranking.Order([
            Entry("primeiro", points: 50, netWorth: 100, lastRound: 10),
            Entry("empatado b", points: 40, netWorth: 100, lastRound: 10, joinedAfterDays: 2),
            Entry("empatado a", points: 40, netWorth: 100, lastRound: 10, joinedAfterDays: 1),
            Entry("quarto", points: 30, netWorth: 100, lastRound: 10),
        ]);

        // A entrada mais antiga decide quem aparece primeiro, não quem está na frente.
        Assert.Equal(["primeiro", "empatado a", "empatado b", "quarto"], Names(ranked));
        Assert.Equal([1, 2, 2, 4], ranked.Select(item => item.Position));
        Assert.Equal([false, true, true, false], ranked.Select(item => item.Tied));
    }

    [Fact]
    public void EveryoneTiedSharesTheFirstPlace()
    {
        var ranked = Ranking.Order([
            Entry("a", points: 0, netWorth: 100, lastRound: null),
            Entry("b", points: 0, netWorth: 100, lastRound: null),
            Entry("c", points: 0, netWorth: 100, lastRound: null),
        ]);

        Assert.All(ranked, item => Assert.Equal(1, item.Position));
        Assert.All(ranked, item => Assert.True(item.Tied));
    }

    [Fact]
    public void TheSameEntriesInAnyOrderGiveTheSameRanking()
    {
        List<RankingEntry> entries =
        [
            Entry("a", points: 40, netWorth: 100, lastRound: 10),
            Entry("b", points: 40, netWorth: 100, lastRound: 10, joinedAfterDays: 3),
            Entry("c", points: 41, netWorth: 90, lastRound: null),
            Entry("d", points: 40, netWorth: 120, lastRound: 2),
        ];

        var direto = Ranking.Order(entries);
        var invertido = Ranking.Order(Enumerable.Reverse(entries));

        Assert.Equal(Names(direto), Names(invertido));
        Assert.Equal(direto.Select(item => item.Position), invertido.Select(item => item.Position));
    }

    [Fact]
    public void EmptyCompetitionHasAnEmptyRanking() => Assert.Empty(Ranking.Order([]));

    private static RankingEntry Entry(
        string name,
        decimal points,
        decimal netWorth,
        decimal? lastRound,
        int joinedAfterDays = 0) =>
        new(Ids.GetOrAdd(name), points, netWorth, lastRound, Start.AddDays(joinedAfterDays));

    private static string[] Names(IEnumerable<RankedEntry> ranked) =>
        [.. ranked.Select(item => Ids.First(pair => pair.Value == item.Entry.EntryId).Key)];

    /// <summary>Nomes viram identificadores estáveis, para os testes lerem como uma tabela.</summary>
    private static readonly Dictionary<string, Guid> Ids = [];
}

file static class DictionaryExtensions
{
    public static Guid GetOrAdd(this Dictionary<string, Guid> source, string key)
    {
        if (!source.TryGetValue(key, out var value))
        {
            value = Guid.CreateVersion7();
            source[key] = value;
        }

        return value;
    }
}
