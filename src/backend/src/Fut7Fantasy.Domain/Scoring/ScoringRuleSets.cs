using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>
/// Catálogo versionado das regras de pontuação, um conjunto por modalidade. Os valores
/// vêm da calibração de 2026-09-16 (01 §9, simulação `modalidades-v1`): gol, assistência,
/// jogo sem sofrer gol e gol sofrido mudam com a modalidade, porque dependem de quantos
/// gols ela produz; os demais eventos são iguais nas três.
/// </summary>
public static class ScoringRuleSets
{
    private static readonly CommonEventPoints CommonV1 = new(
        PenaltySave: 7m,
        GoalkeeperSave: 1m,
        YellowCard: -2m,
        RedCard: -5m,
        OwnGoal: -3m,
        PenaltyMiss: -4m,
        CaptainMultiplier: 2m,
        Valuation: new ValuationBands(
            Falls: [(-6m, -2m), (-3m, -1m), (-1m, -0.5m)],
            Rises: [(1m, 0.5m), (3m, 1m), (6m, 1.5m), (9m, 2m)],
            Floor: SportsAssetPricing.MinimumPrice,
            Ceiling: SportsAssetPricing.MaximumPrice));

    private static readonly ScoringRuleSet[] Versions =
    [
        V1(Modality.Fut7, goals: (10m, 8m, 6m, 5m), assist: 4m, cleanSheet: 5m, goalConceded: -1m),
        V1(Modality.Futsal, goals: (9m, 9m, 5m, 4m), assist: 3m, cleanSheet: 8m, goalConceded: -1.5m),
        V1(Modality.Field, goals: (13m, 10m, 8m, 7m), assist: 5m, cleanSheet: 3m, goalConceded: -2m),
    ];

    /// <summary>Versão vigente de cada modalidade, na ordem da enumeração.</summary>
    public static IReadOnlyList<ScoringRuleSet> Current { get; } =
        [.. Enum.GetValues<Modality>().Select(CurrentFor)];

    /// <summary>Versão que uma rodada ainda não apurada usa.</summary>
    public static ScoringRuleSet CurrentFor(Modality modality) =>
        Versions.Where(rules => rules.Modality == modality).MaxBy(rules => rules.Version)
        ?? throw new ArgumentOutOfRangeException(nameof(modality), modality, "Modalidade sem regra de pontuação.");

    /// <summary>Uma versão específica, como a que uma apuração guardou.</summary>
    public static ScoringRuleSet? Find(Modality modality, int version) =>
        Versions.SingleOrDefault(rules => rules.Modality == modality && rules.Version == version);

    private static ScoringRuleSet V1(
        Modality modality,
        (decimal Goalkeeper, decimal Defender, decimal Midfielder, decimal Forward) goals,
        decimal assist,
        decimal cleanSheet,
        decimal goalConceded) =>
        new(
            modality,
            1,
            new Dictionary<Position, decimal>
            {
                [Position.Goalkeeper] = goals.Goalkeeper,
                [Position.Defender] = goals.Defender,
                [Position.Midfielder] = goals.Midfielder,
                [Position.Forward] = goals.Forward,
            },
            assist,
            cleanSheet,
            goalConceded,
            CommonV1);
}
