using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>
/// Grupo em que o ativo é comparado: a posição do atleta, ou os técnicos, que formam
/// uma posição própria (01 §9).
/// </summary>
public readonly record struct ValuationGroup(AssetKind Kind, Position? Position)
{
    public static ValuationGroup Of(AssetKind kind, Position? position) =>
        kind == AssetKind.Coach ? new(AssetKind.Coach, null) : new(AssetKind.Athlete, position);
}

/// <summary>Um ativo do campeonato na rodada: o que pontuou e quanto vale agora.</summary>
public sealed record ValuationInput(
    AssetKind Kind,
    Guid AssetId,
    Position? Position,
    bool Played,
    decimal Points,
    decimal CurrentPrice);

/// <summary>
/// O que a rodada fez com o preço de um ativo. <see cref="Average"/> e
/// <see cref="Difference"/> são nulos para quem não jogou: esse ativo não entra na média
/// e o preço não muda. É o que a interface usa para explicar a variação —
/// "8,00 pts · 6,11 acima da média dos defensores · +1,5".
/// </summary>
public sealed record PriceChange(
    AssetKind Kind,
    Guid AssetId,
    decimal PreviousPrice,
    decimal? Average,
    decimal? Difference,
    decimal Variation,
    decimal NewPrice);

/// <summary>
/// Valorização relativa à média da posição (01 §9): a média de quem jogou, arredondada
/// para duas casas, meio para longe de zero; a diferença de cada ativo para ela escolhe a
/// faixa; o preço novo fica entre o piso e o teto.
/// </summary>
public static class Valuation
{
    /// <summary>Média arredondada de cada grupo, só com quem jogou. É ela que a apuração guarda.</summary>
    public static IReadOnlyDictionary<ValuationGroup, decimal> Averages(IEnumerable<ValuationInput> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        return assets
            .Where(asset => asset.Played)
            .GroupBy(asset => ValuationGroup.Of(asset.Kind, asset.Position))
            .ToDictionary(group => group.Key, group => ScoringRuleSet.Round(group.Average(asset => asset.Points)));
    }

    public static IReadOnlyList<PriceChange> Apply(ScoringRuleSet rules, IReadOnlyCollection<ValuationInput> assets)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(assets);

        var averages = Averages(assets);
        return
        [
            .. assets.Select(asset =>
            {
                if (!asset.Played)
                {
                    return new PriceChange(
                        asset.Kind, asset.AssetId, asset.CurrentPrice, null, null, 0m, asset.CurrentPrice);
                }

                var average = averages[ValuationGroup.Of(asset.Kind, asset.Position)];
                var difference = asset.Points - average;
                var variation = rules.Valuation.VariationFor(difference);
                return new PriceChange(
                    asset.Kind,
                    asset.AssetId,
                    asset.CurrentPrice,
                    average,
                    difference,
                    variation,
                    rules.Valuation.Apply(asset.CurrentPrice, variation));
            }),
        ];
    }
}
