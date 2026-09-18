using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.SportsCatalog;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Fantasy;

/// <summary>
/// Materializa, de forma idempotente, as escalações completas das rodadas cujo mercado
/// já fechou. O relógio define quando o retrato vale; a primeira operação posterior só
/// persiste esse fato, sem depender de um job ter executado no minuto exato.
/// </summary>
public sealed class LineupSnapshotMaterializer(Fut7FantasyDbContext dbContext, TimeProvider clock)
{
    public async Task EnsureClosedRoundsAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competitionId
                && round.MarketCloseAt != null
                && round.MarketCloseAt <= now
                && round.Status != RoundStatus.Draft
                && round.Status != RoundStatus.Cancelled)
            .OrderBy(round => round.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rounds.Count == 0)
        {
            return;
        }

        var competition = await dbContext.Competitions
            .AsNoTracking()
            .SingleAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        var profile = competition.ModalityProfile;
        var activeTeams = await CatalogAvailability.ActiveRealTeamsAsync(
                dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        var rules = new SquadRules(profile, profile.RealTeamLimitFor(Math.Max(activeTeams, 2)));

        var entries = await dbContext.FantasyEntries
            .AsNoTracking()
            .Include(entry => entry.Slots)
            .Where(entry => entry.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return;
        }

        var existing = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Where(snapshot => rounds.Select(round => round.Id).Contains(snapshot.RoundId))
            .Select(snapshot => new { snapshot.EntryId, snapshot.RoundId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingKeys = existing.Select(item => (item.EntryId, item.RoundId)).ToHashSet();
        var assets = await AssetsAsync(competition, cancellationToken).ConfigureAwait(false);

        List<(Guid EntryId, Guid RoundId)> addedKeys = [];
        foreach (var round in rounds)
        {
            var marketClosedAt = round.MarketCloseAt!.Value;
            foreach (var entry in entries)
            {
                var key = (entry.Id, round.Id);
                if (entry.JoinedAt > marketClosedAt
                    || existingKeys.Contains(key)
                    || entry.Issues(rules).Count > 0)
                {
                    continue;
                }

                dbContext.LineupSnapshots.Add(LineupSnapshot.Capture(
                    Guid.CreateVersion7(),
                    round.Id,
                    entry,
                    rules,
                    assets,
                    marketClosedAt,
                    now));
                existingKeys.Add(key);
                addedKeys.Add(key);
            }
        }

        if (addedKeys.Count == 0)
        {
            return;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // Duas primeiras leituras depois do fechamento podem materializar juntas.
            // O índice único escolhe uma; só absorvemos a corrida se todos os retratos
            // pretendidos realmente estiverem no banco depois dela.
            dbContext.ChangeTracker.Clear();
            var saved = await dbContext.LineupSnapshots
                .AsNoTracking()
                .Where(snapshot => addedKeys.Select(key => key.EntryId).Contains(snapshot.EntryId)
                    && addedKeys.Select(key => key.RoundId).Contains(snapshot.RoundId))
                .Select(snapshot => new { snapshot.EntryId, snapshot.RoundId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var savedKeys = saved.Select(item => (item.EntryId, item.RoundId)).ToHashSet();
            if (addedKeys.Any(key => !savedKeys.Contains(key)))
            {
                throw new InvalidOperationException("Não foi possível materializar as escalações fechadas.", exception);
            }
        }
    }

    private async Task<IReadOnlyDictionary<(AssetKind Kind, Guid Id), SnapshotAsset>> AssetsAsync(
        Competition competition,
        CancellationToken cancellationToken)
    {
        var profile = competition.ModalityProfile;
        var athletes = await (
                from athlete in dbContext.Athletes.AsNoTracking()
                join registration in dbContext.RosterRegistrations.AsNoTracking()
                    on athlete.Id equals registration.AthleteId
                join team in dbContext.RealTeams.AsNoTracking() on registration.RealTeamId equals team.Id
                where athlete.CompetitionId == competition.Id
                select new { Athlete = athlete, Registration = registration, Team = team })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var coaches = await (
                from coach in dbContext.Coaches.AsNoTracking()
                join team in dbContext.RealTeams.AsNoTracking() on coach.RealTeamId equals team.Id
                where coach.CompetitionId == competition.Id
                select new { Coach = coach, Team = team })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<(AssetKind, Guid), SnapshotAsset> assets = [];
        foreach (var item in athletes)
        {
            assets[(AssetKind.Athlete, item.Athlete.Id)] = new SnapshotAsset(
                AssetKind.Athlete,
                item.Athlete.Id,
                item.Athlete.SportingName,
                item.Athlete.Position,
                item.Team.Id,
                item.Team.Name,
                profile.InitialAthletePrice(
                    item.Athlete.Position,
                    item.Registration.PriceTier,
                    item.Registration.InitialPriceOverride));
        }

        foreach (var item in coaches)
        {
            assets[(AssetKind.Coach, item.Coach.Id)] = new SnapshotAsset(
                AssetKind.Coach,
                item.Coach.Id,
                item.Coach.EffectiveName(item.Team.Name),
                null,
                item.Team.Id,
                item.Team.Name,
                profile.InitialCoachPrice(item.Coach.PriceTier, item.Coach.InitialPriceOverride));
        }

        return assets;
    }
}
