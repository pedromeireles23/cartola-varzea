using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Scoring;

public sealed class FantasyScoreService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IFantasyScoreService
{
    public async Task<IReadOnlyList<FantasyRoundSummaryView>?> RoundsAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var competition = await CompetitionAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var rounds = await PublishedRoundsAsync(competition.Id, cancellationToken).ConfigureAwait(false);
        if (rounds.Count == 0)
        {
            return [];
        }

        var entryId = await EntryIdAsync(competition.Id, cancellationToken).ConfigureAwait(false);
        var roundIds = rounds.Select(round => round.Id).ToArray();
        var calculations = await CurrentCalculationsAsync(roundIds, cancellationToken).ConfigureAwait(false);
        var totals = entryId is null
            ? []
            : await dbContext.EntryRoundResults
                .AsNoTracking()
                .Where(result => result.EntryId == entryId && calculations.Values.Contains(result.CalculationId))
                .ToDictionaryAsync(result => result.CalculationId, result => result.Total, cancellationToken)
                .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        return
        [
            .. rounds.Select(round => new FantasyRoundSummaryView(
                round.Id,
                round.Name,
                round.Sequence,
                round.PublishedAt!.Value,
                CompetitionClock.ToLocalText(round.PublishedAt!.Value, competition.TimeZoneId),
                CompetitionClock.ToLocalText(round.ConsolidatesAt!.Value, competition.TimeZoneId),
                now < round.ConsolidatesAt!.Value,
                competition.TimeZoneId,
                calculations.TryGetValue(round.Id, out var calculationId)
                    && totals.TryGetValue(calculationId, out var total)
                        ? total
                        : null)),
        ];
    }

    public async Task<FantasyRoundScoreView?> RoundAsync(
        string slug,
        Guid roundId,
        CancellationToken cancellationToken)
    {
        var competition = await CompetitionAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var round = (await PublishedRoundsAsync(competition.Id, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(item => item.Id == roundId);
        if (round is null)
        {
            return null;
        }

        var calculation = await dbContext.RoundCalculations
            .AsNoTracking()
            .Where(item => item.RoundId == round.Id)
            .OrderByDescending(item => item.Revision)
            .Select(item => new { item.Id, item.Revision })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (calculation is null)
        {
            return null;
        }

        var provisional = clock.GetUtcNow() < round.ConsolidatesAt!.Value;
        var publishedAtLocal = CompetitionClock.ToLocalText(round.PublishedAt!.Value, competition.TimeZoneId);
        var consolidatesAtLocal = CompetitionClock.ToLocalText(round.ConsolidatesAt!.Value, competition.TimeZoneId);
        FantasyRoundScoreView Empty() => new(
            round.Id,
            round.Name,
            publishedAtLocal,
            consolidatesAtLocal,
            provisional,
            competition.TimeZoneId,
            calculation.Revision,
            Played: false,
            0m,
            0m,
            []);

        var entryId = await EntryIdAsync(competition.Id, cancellationToken).ConfigureAwait(false);
        if (entryId is null)
        {
            return Empty();
        }

        var result = await dbContext.EntryRoundResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CalculationId == calculation.Id && item.EntryId == entryId,
                cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Include(item => item.Slots)
            .SingleOrDefaultAsync(
                item => item.EntryId == entryId && item.RoundId == round.Id,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is null || snapshot is null)
        {
            return Empty();
        }

        var slots = await dbContext.EntrySlotResults
            .AsNoTracking()
            .Where(item => item.CalculationId == calculation.Id && item.EntryId == entryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var assetIds = slots.Select(slot => slot.AssetId).ToArray();
        var lines = await dbContext.AthleteScoreLines
            .AsNoTracking()
            .Where(line => line.CalculationId == calculation.Id && assetIds.Contains(line.AthleteId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var prices = await dbContext.AssetPriceChanges
            .AsNoTracking()
            .Where(price => price.CalculationId == calculation.Id && assetIds.Contains(price.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var labels = snapshot.Slots.ToDictionary(slot => (slot.Kind, slot.AssetId));
        var linesByAthlete = lines.ToLookup(line => line.AthleteId);
        return new(
            round.Id,
            round.Name,
            publishedAtLocal,
            consolidatesAtLocal,
            provisional,
            competition.TimeZoneId,
            calculation.Revision,
            Played: true,
            result.Total,
            result.CaptainBonus,
            [
                .. slots
                    .OrderBy(slot => slot.Role)
                    .ThenBy(slot => slot.Position)
                    .ThenBy(
                        slot => labels.TryGetValue((slot.Kind, slot.AssetId), out var label)
                            ? label.AssetName
                            : string.Empty,
                        StringComparer.CurrentCulture)
                    .Select(slot =>
                    {
                        var label = labels.GetValueOrDefault((slot.Kind, slot.AssetId));
                        var price = prices.SingleOrDefault(
                            item => item.Kind == slot.Kind && item.AssetId == slot.AssetId);
                        return new FantasyRoundSlotView(
                            slot.Kind.ToString(),
                            slot.AssetId,
                            label?.AssetName ?? string.Empty,
                            slot.Position?.ToString(),
                            label?.RealTeamName ?? string.Empty,
                            slot.Role.ToString(),
                            slot.Played,
                            slot.Points,
                            slot.Counts,
                            snapshot.CaptainAthleteId == slot.AssetId && slot.Kind == AssetKind.Athlete,
                            slot.Replaces,
                            slot.ReplacedBy,
                            [
                                .. linesByAthlete[slot.AssetId]
                                    .Where(_ => slot.Kind == AssetKind.Athlete)
                                    .OrderBy(line => line.Item)
                                    .Select(line => new FantasyScoreLineView(
                                        line.Item.ToString(), line.Quantity, line.Points)),
                            ],
                            price is null
                                ? null
                                : new FantasyPriceChangeView(
                                    price.Average,
                                    price.Difference,
                                    price.Variation,
                                    price.PreviousPrice,
                                    price.NewPrice));
                    }),
            ]);
    }

    /// <summary>Só campeonato publicado é jogável; rascunho não existe para quem joga.</summary>
    private Task<Competition?> CompetitionAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Slug == slug && item.Status == CompetitionStatus.Published,
                cancellationToken);

    /// <summary>Rodadas publicadas, da mais recente para a mais antiga.</summary>
    private async Task<List<Round>> PublishedRoundsAsync(Guid competitionId, CancellationToken cancellationToken) =>
        await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competitionId
                && round.Status == RoundStatus.Published
                && round.PublishedAt != null
                && round.ConsolidatesAt != null)
            .OrderByDescending(round => round.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, Guid>> CurrentCalculationsAsync(
        Guid[] roundIds,
        CancellationToken cancellationToken)
    {
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

    private async Task<Guid?> EntryIdAsync(Guid competitionId, CancellationToken cancellationToken) =>
        await dbContext.FantasyEntries
            .AsNoTracking()
            .Where(entry => entry.CompetitionId == competitionId && entry.UserId == UserId)
            .Select(entry => (Guid?)entry.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
