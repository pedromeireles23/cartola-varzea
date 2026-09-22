using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Scoring;

/// <summary>
/// O ranking geral acumulado (01 §9). Ele é derivado, não guardado: os pontos vêm da
/// revisão vigente de cada rodada apurada e o patrimônio, do saldo mais o preço atual do
/// elenco. Assim uma correção aparece no ranking no mesmo instante em que é republicada,
/// sem nada para sincronizar.
/// </summary>
public sealed class RankingService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IRankingService
{
    public async Task<RankingView?> GeneralAsync(string slug, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .Where(item => item.Slug == slug && item.Status == CompetitionStatus.Published)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        // Rodada reaberta continua contando: a apuração vigente dela é a que vale até a
        // republicação, como na pontuação do participante.
        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competition.Id && round.PublishedAt != null)
            .OrderBy(round => round.MarketCloseAt)
            .ThenBy(round => round.Sequence)
            .Select(round => new { round.Id, round.Name, round.ConsolidatesAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var last = rounds.Count == 0 ? null : rounds[^1];

        var entries = await (
                from entry in dbContext.FantasyEntries.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on entry.UserId equals user.Id
                where entry.CompetitionId == competition.Id
                select new { entry.Id, entry.UserId, entry.Balance, entry.JoinedAt, user.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return new(competition.Name, rounds.Count, last?.Name, Provisional(last?.ConsolidatesAt), []);
        }

        var current = await CurrentCalculationsAsync(
                [.. rounds.Select(round => round.Id)], cancellationToken)
            .ConfigureAwait(false);
        var results = current.Count == 0
            ? []
            : await dbContext.EntryRoundResults
                .AsNoTracking()
                .Where(result => current.Values.Contains(result.CalculationId))
                .Select(result => new { result.CalculationId, result.EntryId, result.Total })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var totals = results
            .GroupBy(result => result.EntryId)
            .ToDictionary(group => group.Key, group => group.Sum(result => result.Total));
        var lastCalculation = last is null ? (Guid?)null : current.GetValueOrDefault(last.Id);
        var lastPoints = results
            .Where(result => result.CalculationId == lastCalculation)
            .ToDictionary(result => result.EntryId, result => result.Total);
        var netWorth = await NetWorthAsync(competition.Id, entries.Select(entry => entry.Id), cancellationToken)
            .ConfigureAwait(false);

        var ranked = Ranking.Order(entries.Select(entry => new RankingEntry(
            entry.Id,
            totals.GetValueOrDefault(entry.Id),
            entry.Balance + netWorth.GetValueOrDefault(entry.Id),
            lastPoints.TryGetValue(entry.Id, out var points) ? points : null,
            entry.JoinedAt)));
        var byId = entries.ToDictionary(entry => entry.Id);

        return new(
            competition.Name,
            rounds.Count,
            last?.Name,
            Provisional(last?.ConsolidatesAt),
            [
                .. ranked.Select(row =>
                {
                    var entry = byId[row.Entry.EntryId];
                    return new RankingRowView(
                        row.Position,
                        row.Tied,
                        entry.DisplayName,
                        row.Entry.TotalPoints,
                        row.Entry.NetWorth,
                        row.Entry.LastRoundPoints,
                        entry.UserId == currentUser.Id);
                }),
            ]);
    }

    private bool Provisional(DateTimeOffset? consolidatesAt) =>
        consolidatesAt is { } instant && clock.GetUtcNow() < instant;

    /// <summary>A revisão mais alta de cada rodada, que é a que vale.</summary>
    private async Task<Dictionary<Guid, Guid>> CurrentCalculationsAsync(
        Guid[] roundIds,
        CancellationToken cancellationToken)
    {
        if (roundIds.Length == 0)
        {
            return [];
        }

        var calculations = await dbContext.RoundCalculations
            .AsNoTracking()
            .Where(item => roundIds.Contains(item.RoundId))
            .Select(item => new { item.RoundId, item.Revision, item.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return calculations
            .GroupBy(item => item.RoundId)
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.Revision)!.Id);
    }

    /// <summary>
    /// O preço atual do elenco de cada participação. Quem chama soma o saldo: patrimônio
    /// é saldo mais elenco, a mesma conta que a tela do jogo mostra.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> NetWorthAsync(
        Guid competitionId,
        IEnumerable<Guid> entryIds,
        CancellationToken cancellationToken)
    {
        var ids = entryIds.ToArray();
        var slots = await dbContext.SquadSlots
            .AsNoTracking()
            .Where(slot => ids.Contains(slot.EntryId))
            .Select(slot => new { slot.EntryId, slot.Kind, slot.AssetId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (slots.Count == 0)
        {
            return [];
        }

        var prices = await AssetPrices.CurrentAsync(dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        var initial = await InitialPricesAsync(competitionId, cancellationToken).ConfigureAwait(false);
        return slots
            .GroupBy(slot => slot.EntryId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(slot => prices.TryGetValue((slot.Kind, slot.AssetId), out var price)
                    ? price
                    : initial.GetValueOrDefault((slot.Kind, slot.AssetId))));
    }

    /// <summary>
    /// O preço de partida de cada ativo, usado enquanto nenhuma rodada foi apurada — ou
    /// para quem entrou no catálogo depois da última apuração.
    /// </summary>
    private async Task<Dictionary<(AssetKind Kind, Guid Id), decimal>> InitialPricesAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var profile = (await dbContext.Competitions
                .AsNoTracking()
                .SingleAsync(item => item.Id == competitionId, cancellationToken)
                .ConfigureAwait(false))
            .ModalityProfile;
        var athletes = await (
                from athlete in dbContext.Athletes.AsNoTracking()
                join registration in dbContext.RosterRegistrations.AsNoTracking()
                    on athlete.Id equals registration.AthleteId
                where athlete.CompetitionId == competitionId
                select new
                {
                    athlete.Id,
                    athlete.Position,
                    registration.PriceTier,
                    registration.InitialPriceOverride,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var coaches = await dbContext.Coaches
            .AsNoTracking()
            .Where(coach => coach.CompetitionId == competitionId)
            .Select(coach => new { coach.Id, coach.PriceTier, coach.InitialPriceOverride })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<(AssetKind, Guid), decimal> prices = [];
        foreach (var athlete in athletes)
        {
            prices[(AssetKind.Athlete, athlete.Id)] = profile.InitialAthletePrice(
                athlete.Position, athlete.PriceTier, athlete.InitialPriceOverride);
        }

        foreach (var coach in coaches)
        {
            prices[(AssetKind.Coach, coach.Id)] = profile.InitialCoachPrice(
                coach.PriceTier, coach.InitialPriceOverride);
        }

        return prices;
    }
}
