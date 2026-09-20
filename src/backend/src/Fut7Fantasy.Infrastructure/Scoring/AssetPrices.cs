using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Scoring;

/// <summary>
/// Preço atual dos ativos de um campeonato: o preço novo gravado na apuração da rodada
/// mais recente, na última revisão dela. Toda apuração grava uma linha para cada ativo do
/// catálogo, então basta a mais recente. Ativo sem linha — nenhuma rodada publicada, ou
/// inscrito depois dela — fica com o preço inicial, que quem chama já sabe calcular.
/// </summary>
internal static class AssetPrices
{
    /// <summary>
    /// Preços que valem agora ou, com <paramref name="before"/>, os que valiam antes da
    /// rodada que fecha naquele instante — a base da valorização dela.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(AssetKind Kind, Guid Id), decimal>> CurrentAsync(
        Fut7FantasyDbContext dbContext,
        Guid competitionId,
        CancellationToken cancellationToken,
        DateTimeOffset? before = null)
    {
        var latest = await (
                from calculation in dbContext.RoundCalculations.AsNoTracking()
                join round in dbContext.Rounds.AsNoTracking() on calculation.RoundId equals round.Id
                where calculation.CompetitionId == competitionId
                    && (before == null || round.MarketCloseAt < before)
                orderby round.MarketCloseAt descending, round.Sequence descending, calculation.Revision descending
                select (Guid?)calculation.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (latest is null)
        {
            return new Dictionary<(AssetKind, Guid), decimal>();
        }

        return await dbContext.AssetPriceChanges
            .AsNoTracking()
            .Where(change => change.CalculationId == latest)
            .ToDictionaryAsync(change => (change.Kind, change.AssetId), change => change.NewPrice, cancellationToken)
            .ConfigureAwait(false);
    }
}
