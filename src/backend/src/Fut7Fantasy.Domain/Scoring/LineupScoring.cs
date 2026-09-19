using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>Vaga do retrato como a apuração precisa dela.</summary>
public sealed record FrozenLineupSlot(
    AssetKind Kind,
    Guid AssetId,
    string Name,
    Position? Position,
    Guid RealTeamId,
    SquadRole Role);

/// <summary>A escalação congelada de uma participação numa rodada, pronta para apurar.</summary>
public sealed record FrozenLineup(Guid CaptainAthleteId, IReadOnlyList<FrozenLineupSlot> Slots)
{
    public static FrozenLineup From(LineupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.CaptainAthleteId,
            [
                .. snapshot.Slots.Select(slot => new FrozenLineupSlot(
                    slot.Kind, slot.AssetId, slot.AssetName, slot.Position, slot.RealTeamId, slot.Role)),
            ]);
    }
}

/// <summary>
/// Uma vaga depois da apuração. <see cref="Counts"/> diz se os pontos entram no total:
/// titular que jogou, reserva que entrou (<see cref="Replaces"/>) e técnico cujo time teve
/// alguém em campo. Titular que não jogou fica com zero e, se o banco o cobriu,
/// <see cref="ReplacedBy"/> diz por quem.
/// </summary>
public sealed record LineupScoreLine(
    AssetKind Kind,
    Guid AssetId,
    SquadRole Role,
    Position? Position,
    bool Played,
    decimal Points,
    bool Counts,
    Guid? Replaces,
    Guid? ReplacedBy);

/// <summary>
/// O resultado de uma escalação na rodada. <see cref="CaptainBonus"/> é o que o
/// multiplicador acrescentou; é zero quando o capitão não jogou.
/// </summary>
public sealed record LineupRoundScore(decimal Total, decimal CaptainBonus, IReadOnlyList<LineupScoreLine> Lines);

/// <summary>
/// Apura uma escalação congelada (01 §9): o reserva só entra no lugar de um titular da
/// mesma posição que não jogou, e só se ele mesmo jogou; o capitão que jogou multiplica
/// os próprios pontos; capitão que não jogou não gera bônus, e o reserva que entra no
/// lugar dele nunca herda o multiplicador; o técnico vale a média do time dele.
/// </summary>
public static class LineupScoring
{
    public static LineupRoundScore Score(
        ScoringRuleSet rules,
        FrozenLineup lineup,
        IReadOnlyDictionary<Guid, AthleteRoundScore> athletes,
        IReadOnlyDictionary<Guid, decimal> coaches)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(lineup);
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(coaches);

        bool Played(FrozenLineupSlot slot) =>
            slot.Kind == AssetKind.Coach
                ? coaches.ContainsKey(slot.RealTeamId)
                : athletes.TryGetValue(slot.AssetId, out var score) && score.Played;

        decimal Points(FrozenLineupSlot slot) =>
            slot.Kind == AssetKind.Coach
                ? coaches.GetValueOrDefault(slot.RealTeamId)
                : Played(slot) ? athletes[slot.AssetId].Points : 0m;

        // Ordem fixa pelo nome e pelo identificador: dois titulares da mesma posição fora
        // de campo têm de dar sempre a mesma substituição, em qualquer recálculo.
        var ordered = lineup.Slots
            .OrderBy(slot => slot.Name, StringComparer.Ordinal)
            .ThenBy(slot => slot.AssetId)
            .ToList();

        Dictionary<Guid, Guid> replacements = [];
        foreach (var position in Enum.GetValues<Position>())
        {
            var absent = ordered.FirstOrDefault(slot =>
                slot.Role == SquadRole.Starter && slot.Position == position && !Played(slot));
            var reserve = ordered.FirstOrDefault(slot =>
                slot.Role == SquadRole.Bench && slot.Position == position && Played(slot));
            if (absent is not null && reserve is not null)
            {
                replacements[absent.AssetId] = reserve.AssetId;
            }
        }

        var replacedStarter = replacements.ToDictionary(pair => pair.Value, pair => pair.Key);
        var lines = ordered
            .OrderBy(slot => slot.Role)
            .ThenBy(slot => slot.Position)
            .Select(slot =>
            {
                var played = Played(slot);
                Guid? replaces = replacedStarter.TryGetValue(slot.AssetId, out var starter) ? starter : null;
                Guid? replacedBy = replacements.TryGetValue(slot.AssetId, out var reserve) ? reserve : null;
                var counts = slot.Role switch
                {
                    SquadRole.Bench => replaces is not null,
                    _ => played,
                };
                return new LineupScoreLine(
                    slot.Kind,
                    slot.AssetId,
                    slot.Role,
                    slot.Position,
                    played,
                    Points(slot),
                    counts,
                    replaces,
                    replacedBy);
            })
            .ToList();

        var captain = lines.SingleOrDefault(line =>
            line.Role == SquadRole.Starter && line.AssetId == lineup.CaptainAthleteId);
        var captainBonus = captain is { Played: true }
            ? ScoringRuleSet.Round(captain.Points * (rules.CaptainMultiplier - 1m))
            : 0m;

        return new LineupRoundScore(
            lines.Where(line => line.Counts).Sum(line => line.Points) + captainBonus,
            captainBonus,
            lines);
    }
}
