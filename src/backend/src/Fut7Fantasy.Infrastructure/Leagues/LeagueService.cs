using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Leagues;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Leagues;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Competitions;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Leagues;

/// <summary>
/// Ligas privadas (01 §9). A liga recorta o ranking geral entre quem se conhece: mesma
/// pontuação, mesma ordenação, outro conjunto de participações.
///
/// Toda leitura e toda escrita é da conta da sessão. Entrar exige já jogar o campeonato
/// da liga, o que também garante que a equipe de um campeonato nunca dispute a liga de
/// outro (04 §6).
/// </summary>
public sealed class LeagueService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ILeagueService
{
    private readonly CompetitionStandings _standings = new(dbContext);

    public async Task<IReadOnlyList<LeagueSummaryView>?> MineAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var standings = await _standings.BySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (standings is null)
        {
            return null;
        }

        var leagues = await (
                from membership in dbContext.LeagueMemberships.AsNoTracking()
                join league in dbContext.PrivateLeagues.AsNoTracking() on membership.LeagueId equals league.Id
                where membership.UserId == UserId && league.CompetitionId == standings.CompetitionId
                orderby league.Name
                select league)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (leagues.Count == 0)
        {
            return [];
        }

        var ids = leagues.Select(league => league.Id).ToArray();
        var members = await dbContext.LeagueMemberships
            .AsNoTracking()
            .Where(membership => ids.Contains(membership.LeagueId))
            .Select(membership => new { membership.LeagueId, membership.EntryId, membership.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byLeague = members.ToLookup(membership => membership.LeagueId);
        var timeZone = await TimeZoneAsync(standings.CompetitionId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. leagues.Select(league =>
            {
                var dentro = byLeague[league.Id].ToList();
                var ranked = Rank(standings, dentro.Select(membership => membership.EntryId));
                var minha = ranked.FirstOrDefault(row =>
                    dentro.Single(membership => membership.EntryId == row.Entry.EntryId).UserId == UserId);
                var dono = league.OwnerUserId == UserId;
                return new LeagueSummaryView(
                    league.Id,
                    league.Name,
                    slug,
                    dentro.Count,
                    dono,
                    minha?.Position,
                    dono ? league.InviteCode : null,
                    dono ? Local(league.InviteExpiresAt, timeZone) : null,
                    Convert.ToBase64String(league.RowVersion));
            }),
        ];
    }

    public async Task<LeagueCommandResult> CreateAsync(
        string slug,
        LeagueDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return new(LeagueCommandOutcome.Invalid, null, errors);
        }

        var competition = await dbContext.Competitions
            .AsNoTracking()
            .Where(item => item.Slug == slug && item.Status == CompetitionStatus.Published)
            .Select(item => item.Id)
            .Cast<Guid?>()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (competition is not { } competitionId)
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.NotFound);
        }

        // Liga é um recorte de quem disputa: sem equipe no campeonato não há o que somar.
        var entry = await EntryAsync(competitionId, cancellationToken).ConfigureAwait(false);
        if (entry is not { } entryId)
        {
            return LeagueCommandResult.Invalid(
                new LeagueError("Entry", "Entre no campeonato antes de criar uma liga nele."));
        }

        var now = clock.GetUtcNow();
        var league = PrivateLeague.Create(
            Guid.CreateVersion7(), competitionId, UserId, definition, LeagueInviteCode.Generate(), now);
        dbContext.PrivateLeagues.Add(league);
        dbContext.LeagueMemberships.Add(
            LeagueMembership.Create(Guid.CreateVersion7(), league.Id, entryId, UserId, now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Inclui o código repetido, que o índice único recusa; tentar de novo resolve.
            dbContext.ChangeTracker.Clear();
            return LeagueCommandResult.Of(LeagueCommandOutcome.Conflict);
        }

        var minhas = await MineAsync(slug, cancellationToken).ConfigureAwait(false);
        return new(
            LeagueCommandOutcome.Completed,
            minhas?.SingleOrDefault(item => item.Id == league.Id),
            []);
    }

    public async Task<LeagueView?> GetAsync(
        string slug,
        Guid leagueId,
        CancellationToken cancellationToken)
    {
        var league = await dbContext.PrivateLeagues
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);
        if (league is null || !await BelongsAsync(league, slug, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var members = await dbContext.LeagueMemberships
            .AsNoTracking()
            .Where(membership => membership.LeagueId == leagueId)
            .Select(membership => new { membership.Id, membership.EntryId, membership.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Quem não é membro recebe o mesmo "não encontrado" de uma liga inexistente: a
        // resposta não pode confirmar que aquela liga existe.
        if (!members.Exists(membership => membership.UserId == UserId))
        {
            return null;
        }

        var standings = await _standings.ByIdAsync(league.CompetitionId, cancellationToken).ConfigureAwait(false);
        if (standings is null)
        {
            return null;
        }

        var timeZone = await TimeZoneAsync(league.CompetitionId, cancellationToken).ConfigureAwait(false);
        var dono = league.OwnerUserId == UserId;
        var byEntry = standings.Rows.ToDictionary(row => row.EntryId);

        return new(
            league.Id,
            league.Name,
            standings.CompetitionName,
            slug,
            dono,
            dono ? league.InviteCode : null,
            dono ? Local(league.InviteExpiresAt, timeZone) : null,
            standings.Rounds,
            standings.LastRoundName,
            standings.ConsolidatesAt is { } instant && clock.GetUtcNow() < instant,
            [
                .. Rank(standings, members.Select(membership => membership.EntryId)).Select(ranked =>
                {
                    var membership = members.Single(item => item.EntryId == ranked.Entry.EntryId);
                    var row = byEntry[ranked.Entry.EntryId];
                    var eu = membership.UserId == UserId;
                    return new LeagueMemberView(
                        // O identificador da associação é o que a remoção usa; ele só vai
                        // para quem pode agir naquela linha.
                        dono || eu ? membership.Id : null,
                        ranked.Position,
                        ranked.Tied,
                        row.DisplayName,
                        ranked.Entry.TotalPoints,
                        ranked.Entry.NetWorth,
                        ranked.Entry.LastRoundPoints,
                        eu,
                        membership.UserId == league.OwnerUserId);
                }),
            ],
            Convert.ToBase64String(league.RowVersion));
    }

    public async Task<LeagueCommandResult> JoinAsync(string? code, CancellationToken cancellationToken)
    {
        // Um texto que não pode ser um código nem chega ao banco: é a primeira barreira
        // contra quem fica tentando.
        var normalized = LeagueInviteCode.Normalize(code);
        if (normalized is null)
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.NotFound);
        }

        var league = await dbContext.PrivateLeagues
            .SingleOrDefaultAsync(item => item.InviteCode == normalized, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (league is null || !league.AcceptsJoin(now))
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.NotFound);
        }

        var entry = await EntryAsync(league.CompetitionId, cancellationToken).ConfigureAwait(false);
        if (entry is not { } entryId)
        {
            return LeagueCommandResult.Invalid(
                new LeagueError("Entry", "Entre no campeonato desta liga antes de usar o código."));
        }

        var slug = await dbContext.Competitions
            .AsNoTracking()
            .Where(item => item.Id == league.CompetitionId)
            .Select(item => item.Slug!)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var members = await dbContext.LeagueMemberships
            .CountAsync(membership => membership.LeagueId == league.Id, cancellationToken)
            .ConfigureAwait(false);

        // Entrar de novo não é erro: devolve a liga, que é o que a pessoa queria ver.
        if (await dbContext.LeagueMemberships
            .AnyAsync(
                membership => membership.LeagueId == league.Id && membership.EntryId == entryId,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return await CompletedAsync(slug, league.Id, cancellationToken).ConfigureAwait(false);
        }

        if (members >= PrivateLeague.MaxMembers)
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.LimitReached);
        }

        dbContext.LeagueMemberships.Add(
            LeagueMembership.Create(Guid.CreateVersion7(), league.Id, entryId, UserId, now));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Duas tentativas ao mesmo tempo: o índice único recusa a segunda, e quem
            // pediu já está dentro de qualquer jeito.
            dbContext.ChangeTracker.Clear();
        }

        return await CompletedAsync(slug, league.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LeagueCommandResult> RotateInviteAsync(
        string slug,
        Guid leagueId,
        bool close,
        string version,
        CancellationToken cancellationToken)
    {
        var league = await dbContext.PrivateLeagues
            .SingleOrDefaultAsync(item => item.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);
        if (league is null || !await BelongsAsync(league, slug, cancellationToken).ConfigureAwait(false))
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.NotFound);
        }

        if (league.OwnerUserId != UserId)
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.Forbidden);
        }

        if (!RowVersions.Matches(version, league.RowVersion, out var expected))
        {
            return LeagueCommandResult.Of(LeagueCommandOutcome.Conflict);
        }

        var now = clock.GetUtcNow();
        if (close)
        {
            league.CloseInvite(now);
        }
        else
        {
            league.RotateInvite(LeagueInviteCode.Generate(), now);
        }

        dbContext.Entry(league).Property(item => item.RowVersion).OriginalValue = expected;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return LeagueCommandResult.Of(LeagueCommandOutcome.Conflict);
        }

        return await CompletedAsync(slug, league.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LeagueCommandOutcome> RemoveMemberAsync(
        string slug,
        Guid leagueId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var league = await dbContext.PrivateLeagues
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);
        var membership = await dbContext.LeagueMemberships
            .SingleOrDefaultAsync(
                item => item.Id == membershipId && item.LeagueId == leagueId, cancellationToken)
            .ConfigureAwait(false);
        if (league is null
            || membership is null
            || !await BelongsAsync(league, slug, cancellationToken).ConfigureAwait(false))
        {
            return LeagueCommandOutcome.NotFound;
        }

        // O dono remove qualquer um; qualquer membro sai sozinho.
        var dono = league.OwnerUserId == UserId;
        if (!dono && membership.UserId != UserId)
        {
            return LeagueCommandOutcome.Forbidden;
        }

        // O dono não sai da própria liga: liga sem dono não existe, então ele a apaga.
        if (membership.UserId == league.OwnerUserId)
        {
            return LeagueCommandOutcome.Forbidden;
        }

        dbContext.LeagueMemberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return LeagueCommandOutcome.Completed;
    }

    public async Task<LeagueCommandOutcome> DeleteAsync(
        string slug,
        Guid leagueId,
        string version,
        CancellationToken cancellationToken)
    {
        var league = await dbContext.PrivateLeagues
            .SingleOrDefaultAsync(item => item.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);
        if (league is null || !await BelongsAsync(league, slug, cancellationToken).ConfigureAwait(false))
        {
            return LeagueCommandOutcome.NotFound;
        }

        if (league.OwnerUserId != UserId)
        {
            return LeagueCommandOutcome.Forbidden;
        }

        if (!RowVersions.Matches(version, league.RowVersion, out _))
        {
            return LeagueCommandOutcome.Conflict;
        }

        // As associações vão junto pela cascata configurada na liga.
        dbContext.PrivateLeagues.Remove(league);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return LeagueCommandOutcome.Conflict;
        }

        return LeagueCommandOutcome.Completed;
    }

    /// <summary>A ordenação aprovada, aplicada só às participações que estão na liga.</summary>
    private static IReadOnlyList<RankedEntry> Rank(Standings standings, IEnumerable<Guid> entryIds)
    {
        var dentro = entryIds.ToHashSet();
        return Ranking.Order(standings.Rows
            .Where(row => dentro.Contains(row.EntryId))
            .Select(row => row.Entry));
    }

    private async Task<LeagueCommandResult> CompletedAsync(
        string slug,
        Guid leagueId,
        CancellationToken cancellationToken)
    {
        var minhas = await MineAsync(slug, cancellationToken).ConfigureAwait(false);
        return new(
            LeagueCommandOutcome.Completed,
            minhas?.SingleOrDefault(item => item.Id == leagueId),
            []);
    }

    /// <summary>
    /// A liga é mesmo do campeonato nomeado no endereço? Sem esta conferência o slug da
    /// rota seria enfeite, e o mesmo identificador responderia sob qualquer campeonato.
    /// Divergência responde como inexistente, pelo mesmo motivo de quem não é membro: a
    /// resposta não confirma em que campeonato aquela liga está.
    /// </summary>
    private Task<bool> BelongsAsync(PrivateLeague league, string slug, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .AnyAsync(item => item.Id == league.CompetitionId && item.Slug == slug, cancellationToken);

    private Task<Guid?> EntryAsync(Guid competitionId, CancellationToken cancellationToken) =>
        dbContext.FantasyEntries
            .AsNoTracking()
            .Where(entry => entry.CompetitionId == competitionId && entry.UserId == UserId)
            .Select(entry => (Guid?)entry.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<string> TimeZoneAsync(Guid competitionId, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => item.TimeZoneId)
            .SingleAsync(cancellationToken);

    private static string? Local(DateTimeOffset? instant, string timeZoneId) =>
        instant is { } value ? CompetitionClock.ToLocalText(value, timeZoneId) : null;

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
