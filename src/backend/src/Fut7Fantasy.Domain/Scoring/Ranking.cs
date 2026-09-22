namespace Fut7Fantasy.Domain.Scoring;

/// <summary>
/// Os números de uma participação que decidem a ordem do ranking (01 §9).
///
/// <see cref="LastRoundPoints"/> é nulo para quem não jogou a rodada mais recente; para
/// desempatar isso vale menos que qualquer pontuação, inclusive negativa, porque quem não
/// entrou em campo não pode passar na frente de quem entrou e foi mal.
/// </summary>
public sealed record RankingEntry(
    Guid EntryId,
    decimal TotalPoints,
    decimal NetWorth,
    decimal? LastRoundPoints,
    DateTimeOffset JoinedAt);

/// <summary>
/// Uma participação na lista. <see cref="Position"/> é a colocação, que empatados
/// dividem; a ordem dentro do empate é a da lista devolvida.
/// </summary>
public sealed record RankedEntry(int Position, bool Tied, RankingEntry Entry);

/// <summary>
/// A ordem do ranking, geral ou de liga: as mesmas entradas dão sempre a mesma lista.
///
/// Ordena por pontos totais, patrimônio atual e pontos da rodada mais recente; quem
/// empata nos três divide a colocação (1º, 2º, 2º, 4º), e a entrada mais antiga decide
/// apenas quem aparece primeiro dentro do empate — decisão do Pedro em 2026-09-22, porque
/// duas pessoas com números idênticos estão empatadas em tudo que é esportivo, e a hora
/// em que cada uma se inscreveu não é mérito. O identificador fecha a ordem para que o
/// desempate nunca dependa de sorteio.
/// </summary>
public static class Ranking
{
    public static IReadOnlyList<RankedEntry> Order(IEnumerable<RankingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries
            .OrderByDescending(entry => entry.TotalPoints)
            .ThenByDescending(entry => entry.NetWorth)
            .ThenByDescending(entry => entry.LastRoundPoints ?? decimal.MinValue)
            .ThenBy(entry => entry.JoinedAt)
            .ThenBy(entry => entry.EntryId)
            .ToList();

        List<RankedEntry> ranked = [];
        var position = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];
            var tiedWithPrevious = index > 0 && SameStanding(ordered[index - 1], entry);
            if (!tiedWithPrevious)
            {
                // Colocação de competição: depois de dois segundos lugares vem o quarto.
                position = index + 1;
            }

            var tiedWithNext = index + 1 < ordered.Count && SameStanding(entry, ordered[index + 1]);
            ranked.Add(new(position, tiedWithPrevious || tiedWithNext, entry));
        }

        return ranked;
    }

    /// <summary>Empate é a igualdade nos três critérios esportivos, sem a data de entrada.</summary>
    private static bool SameStanding(RankingEntry left, RankingEntry right) =>
        left.TotalPoints == right.TotalPoints
        && left.NetWorth == right.NetWorth
        && left.LastRoundPoints == right.LastRoundPoints;
}
