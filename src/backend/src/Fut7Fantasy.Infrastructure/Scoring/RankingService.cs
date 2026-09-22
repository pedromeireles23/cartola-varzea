using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Persistence;

namespace Fut7Fantasy.Infrastructure.Scoring;

/// <summary>
/// O ranking geral acumulado (01 §9): a foto do campeonato inteira, ordenada pelos
/// critérios aprovados. A liga privada usa a mesma foto e a mesma ordenação, filtrando
/// as participações que estão nela.
/// </summary>
public sealed class RankingService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IRankingService
{
    private readonly CompetitionStandings _standings = new(dbContext);

    public async Task<RankingView?> GeneralAsync(string slug, CancellationToken cancellationToken)
    {
        var standings = await _standings.BySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (standings is null)
        {
            return null;
        }

        var byId = standings.Rows.ToDictionary(row => row.EntryId);
        return new(
            standings.CompetitionName,
            standings.Rounds,
            standings.LastRoundName,
            standings.ConsolidatesAt is { } instant && clock.GetUtcNow() < instant,
            [
                .. Ranking.Order(standings.Rows.Select(row => row.Entry)).Select(ranked =>
                {
                    var row = byId[ranked.Entry.EntryId];
                    return new RankingRowView(
                        ranked.Position,
                        ranked.Tied,
                        row.DisplayName,
                        ranked.Entry.TotalPoints,
                        ranked.Entry.NetWorth,
                        ranked.Entry.LastRoundPoints,
                        row.UserId == currentUser.Id);
                }),
            ]);
    }
}
