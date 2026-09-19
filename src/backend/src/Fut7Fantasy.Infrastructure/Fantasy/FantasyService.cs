using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Fantasy;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.SportsCatalog;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Fantasy;

public sealed class FantasyService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock,
    LineupSnapshotMaterializer snapshotMaterializer) : IFantasyService
{
    public async Task<FantasyOverview?> OverviewAsync(string slug, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(slug, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var entry = await EntryAsync(context.Competition.Id, tracked: false, cancellationToken).ConfigureAwait(false);
        return await ViewAsync(context, entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FantasyCommandResult> JoinAsync(string slug, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(slug, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return FantasyCommandResult.Of(FantasyCommandOutcome.NotFound);
        }

        var entry = await EntryAsync(context.Competition.Id, tracked: false, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            // Aderir fora do mercado é permitido: a conta só pontua a partir da próxima
            // rodada com mercado aberto (01 §9), porque só nela consegue escalar.
            entry = FantasyEntry.Join(
                Guid.CreateVersion7(),
                context.Competition.Id,
                UserId,
                context.Competition.ModalityProfile,
                clock.GetUtcNow());
            dbContext.FantasyEntries.Add(entry);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Duplo clique: o índice único guardou a primeira adesão, que é a que vale.
                dbContext.ChangeTracker.Clear();
                entry = await EntryAsync(context.Competition.Id, tracked: false, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new(
            FantasyCommandOutcome.Completed,
            await ViewAsync(context, entry, cancellationToken).ConfigureAwait(false),
            null);
    }

    public async Task<MarketView?> MarketAsync(string slug, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(slug, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var entry = await EntryAsync(context.Competition.Id, tracked: false, cancellationToken).ConfigureAwait(false);
        var items = context.Catalog.Values
            .OrderBy(asset => asset.RealTeamName, StringComparer.CurrentCulture)
            .ThenBy(asset => asset.Market.Kind)
            .ThenBy(asset => asset.Market.Position)
            .ThenBy(asset => asset.Name, StringComparer.CurrentCulture)
            .Select(asset =>
            {
                var block = BlockFor(context, entry, asset.Market);
                return new MarketItemView(
                    asset.Market.Kind.ToString(),
                    asset.Market.Id,
                    asset.Name,
                    asset.Market.Position?.ToString(),
                    asset.Market.RealTeamId,
                    asset.RealTeamName,
                    asset.Market.Price,
                    asset.Market.IsAvailable,
                    entry?.Owns(asset.Market.Kind, asset.Market.Id) ?? false,
                    block?.Code,
                    block?.Message);
            })
            .ToList();

        return new MarketView(entry?.Balance, context.Market, items);
    }

    public Task<FantasyCommandResult> BuyAsync(
        string slug,
        AssetKind kind,
        Guid assetId,
        CancellationToken cancellationToken) =>
        MutateAsync(slug, (context, entry, now) =>
            context.Catalog.TryGetValue((kind, assetId), out var asset)
                ? entry.Buy(asset.Market, context.Rules, now)
                : new SquadRejection("not_found", "Esse ativo não existe neste campeonato."),
            cancellationToken);

    public Task<FantasyCommandResult> SellAsync(
        string slug,
        AssetKind kind,
        Guid assetId,
        CancellationToken cancellationToken) =>
        MutateAsync(slug, (context, entry, now) =>
            context.Catalog.TryGetValue((kind, assetId), out var asset)
                ? entry.Sell(kind, assetId, asset.Market.Price, now)
                : new SquadRejection("not_owned", "Esse ativo não está no seu elenco."),
            cancellationToken);

    public Task<FantasyCommandResult> SwapAsync(
        string slug,
        Guid starterAthleteId,
        Guid benchAthleteId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            slug,
            (context, entry, now) => entry.Swap(starterAthleteId, benchAthleteId, context.Rules, now),
            cancellationToken);

    public Task<FantasyCommandResult> SetCaptainAsync(
        string slug,
        Guid athleteId,
        CancellationToken cancellationToken) =>
        MutateAsync(slug, (_, entry, now) => entry.SetCaptain(athleteId, now), cancellationToken);

    /// <summary>
    /// Toda mudança no elenco passa por aqui: exige adesão e mercado aberto pelo relógio do
    /// servidor, e grava contra a versão lida da participação. Duas requisições simultâneas
    /// leem a mesma versão; a segunda falha em vez de gastar o saldo duas vezes (04 §8).
    /// </summary>
    private async Task<FantasyCommandResult> MutateAsync(
        string slug,
        Func<FantasyContext, FantasyEntry, DateTimeOffset, SquadRejection?> change,
        CancellationToken cancellationToken)
    {
        var context = await ContextAsync(slug, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return FantasyCommandResult.Of(FantasyCommandOutcome.NotFound);
        }

        var entry = await EntryAsync(context.Competition.Id, tracked: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return FantasyCommandResult.Of(FantasyCommandOutcome.NotJoined);
        }

        if (!context.Market.IsOpen)
        {
            return FantasyCommandResult.Of(FantasyCommandOutcome.MarketClosed);
        }

        if (change(context, entry, clock.GetUtcNow()) is { } rejection)
        {
            return new(FantasyCommandOutcome.Rejected, null, rejection);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Inclui a concorrência e o índice único do ativo: nada foi gravado.
            return FantasyCommandResult.Of(FantasyCommandOutcome.Conflict);
        }

        return new(
            FantasyCommandOutcome.Completed,
            await ViewAsync(context, entry, cancellationToken).ConfigureAwait(false),
            null);
    }

    private static SquadRejection? BlockFor(FantasyContext context, FantasyEntry? entry, MarketAsset asset)
    {
        if (entry is null)
        {
            return new("not_joined", "Entre no campeonato para comprar.");
        }

        if (!context.Market.IsOpen)
        {
            return new("market_closed", "O mercado está fechado.");
        }

        return entry.CanBuy(asset, context.Rules);
    }

    private async Task<FantasyOverview> ViewAsync(
        FantasyContext context,
        FantasyEntry? entry,
        CancellationToken cancellationToken) => new(
        context.Competition.Name,
        context.Competition.Slug!,
        ModalityProfileView.From(context.Competition.ModalityProfile),
        context.Market,
        new FantasyTeamLimitView(
            context.ActiveRealTeams, context.Rules.TeamLimit.MaxStarters, context.Rules.TeamLimit.MaxAthletes),
        entry is null
            ? null
            : EntryView(
                context,
                entry,
                await LastClosedRoundAsync(context, entry, cancellationToken).ConfigureAwait(false)));

    private static FantasyEntryView EntryView(
        FantasyContext context,
        FantasyEntry entry,
        ClosedRoundLineupView? lastClosedRound)
    {
        var slots = entry.Slots
            .Select(slot =>
            {
                var asset = context.Catalog[(slot.Kind, slot.AssetId)];
                return new SquadSlotView(
                    slot.Kind.ToString(),
                    slot.AssetId,
                    asset.Name,
                    slot.Position?.ToString(),
                    slot.RealTeamId,
                    asset.RealTeamName,
                    slot.Role.ToString(),
                    asset.Market.Price,
                    slot.PurchasePrice,
                    asset.Market.IsAvailable,
                    entry.CaptainAthleteId == slot.AssetId);
            })
            .OrderBy(slot => slot.Role)
            .ThenBy(slot => slot.Position)
            .ToList();

        return new FantasyEntryView(
            entry.Balance,
            entry.Balance + slots.Sum(slot => slot.CurrentPrice),
            entry.CaptainAthleteId,
            slots,
            entry.Issues(context.Rules),
            lastClosedRound);
    }

    /// <summary>
    /// O que valeu na rodada mais recente cujo mercado já fechou. <see cref="ContextAsync"/>
    /// materializou os retratos antes, então a falta do retrato já diz tudo: ou a conta
    /// entrou depois do fechamento, ou a escalação estava incompleta nele.
    /// </summary>
    private async Task<ClosedRoundLineupView?> LastClosedRoundAsync(
        FantasyContext context,
        FantasyEntry entry,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var round = await dbContext.Rounds
            .AsNoTracking()
            .Where(item => item.CompetitionId == context.Competition.Id
                && item.MarketCloseAt != null
                && item.MarketCloseAt <= now
                && item.Status != RoundStatus.Draft
                && item.Status != RoundStatus.Cancelled)
            .OrderByDescending(item => item.MarketCloseAt)
            .ThenByDescending(item => item.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (round is null)
        {
            return null;
        }

        var closedAt = round.MarketCloseAt!.Value;
        var snapshot = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Include(item => item.Slots)
            .SingleOrDefaultAsync(
                item => item.EntryId == entry.Id && item.RoundId == round.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var status = snapshot is not null ? "Frozen"
            : entry.JoinedAt > closedAt ? "JoinedAfterClose"
            : "Incomplete";
        var slots = snapshot is null
            ? []
            : snapshot.Slots
                .OrderBy(slot => slot.Role)
                .ThenBy(slot => slot.Position)
                .Select(slot => new FrozenSlotView(
                    slot.Kind.ToString(),
                    slot.AssetId,
                    slot.AssetName,
                    slot.Position?.ToString(),
                    slot.RealTeamId,
                    slot.RealTeamName,
                    slot.Role.ToString(),
                    slot.Price,
                    snapshot.CaptainAthleteId == slot.AssetId))
                .ToList();

        return new ClosedRoundLineupView(
            round.Name,
            closedAt,
            CompetitionClock.ToLocalText(closedAt, context.Competition.TimeZoneId),
            status,
            snapshot?.CaptainAthleteId,
            slots);
    }

    private async Task<FantasyEntry?> EntryAsync(Guid competitionId, bool tracked, CancellationToken cancellationToken)
    {
        var entries = tracked ? dbContext.FantasyEntries : dbContext.FantasyEntries.AsNoTracking();
        return await entries
            .Include(entry => entry.Slots)
            .SingleOrDefaultAsync(
                entry => entry.CompetitionId == competitionId && entry.UserId == UserId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrato do campeonato para a operação: regras vigentes, estado do mercado e o
    /// catálogo com preço atual e disponibilidade. Só campeonato publicado é jogável.
    /// </summary>
    private async Task<FantasyContext?> ContextAsync(string slug, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Slug == slug && item.Status == CompetitionStatus.Published,
                cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        await snapshotMaterializer.EnsureClosedRoundsAsync(competition.Id, cancellationToken)
            .ConfigureAwait(false);

        var profile = competition.ModalityProfile;
        var eliminated = await CatalogAvailability.EliminatedTeamsAsync(dbContext, competition.Id, cancellationToken)
            .ConfigureAwait(false);
        var activeTeams = await CatalogAvailability.ActiveRealTeamsAsync(dbContext, competition.Id, cancellationToken)
            .ConfigureAwait(false);

        // O limite só existe a partir de dois times; com a final decidida, vale o de dois.
        var rules = new SquadRules(profile, profile.RealTeamLimitFor(Math.Max(activeTeams, 2)));

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

        // Preço atual é o inicial até a valorização da Fase 10 existir.
        Dictionary<(AssetKind, Guid), CatalogAsset> catalog = [];
        foreach (var item in athletes)
        {
            var available = item.Registration.IsActive && !item.Team.IsArchived && !eliminated.Contains(item.Team.Id);
            catalog[(AssetKind.Athlete, item.Athlete.Id)] = new(
                new MarketAsset(
                    AssetKind.Athlete,
                    item.Athlete.Id,
                    item.Athlete.Position,
                    item.Team.Id,
                    profile.InitialAthletePrice(
                        item.Athlete.Position,
                        item.Registration.PriceTier,
                        item.Registration.InitialPriceOverride),
                    available),
                item.Athlete.SportingName,
                item.Team.Name);
        }

        foreach (var item in coaches)
        {
            catalog[(AssetKind.Coach, item.Coach.Id)] = new(
                new MarketAsset(
                    AssetKind.Coach,
                    item.Coach.Id,
                    null,
                    item.Team.Id,
                    profile.InitialCoachPrice(item.Coach.PriceTier, item.Coach.InitialPriceOverride),
                    !item.Team.IsArchived && !eliminated.Contains(item.Team.Id)),
                item.Coach.EffectiveName(item.Team.Name),
                item.Team.Name);
        }

        return new FantasyContext(
            competition,
            rules,
            activeTeams,
            await MarketAsync(competition, cancellationToken).ConfigureAwait(false),
            catalog);
    }

    /// <summary>O mercado é um só: a rodada que está com ele aberto agora, pelo relógio do servidor.</summary>
    private async Task<FantasyMarketStatus> MarketAsync(Competition competition, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competition.Id && round.Status == RoundStatus.MarketOpen)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var open = rounds
            .Where(round => round.PhaseAt(now) == RoundPhase.MarketOpen)
            .MinBy(round => round.MarketCloseAt);

        return open is null
            ? new FantasyMarketStatus(false, null, null, null, competition.TimeZoneId)
            : new FantasyMarketStatus(
                true,
                open.Name,
                open.MarketCloseAt,
                CompetitionClock.ToLocalText(open.MarketCloseAt!.Value, competition.TimeZoneId),
                competition.TimeZoneId);
    }

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

    private sealed record CatalogAsset(MarketAsset Market, string Name, string RealTeamName);

    private sealed record FantasyContext(
        Competition Competition,
        SquadRules Rules,
        int ActiveRealTeams,
        FantasyMarketStatus Market,
        Dictionary<(AssetKind, Guid), CatalogAsset> Catalog);
}
