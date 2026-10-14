using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.SportsCatalog;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class CompetitionManagementService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICompetitionManagementService
{
    /// <summary>Do mercado aberto até a publicação: a rodada pede alguma coisa da organização.</summary>
    private static readonly RoundPhase[] InPlay =
    [
        RoundPhase.MarketOpen,
        RoundPhase.MarketClosed,
        RoundPhase.InProgress,
        RoundPhase.UnderReview,
        RoundPhase.ReopenedForCorrection,
    ];

    public async Task<IReadOnlyList<CompetitionSummaryView>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // O filtro por organização é explícito mesmo com a policy na rota (03 §8).
        var competitions = await dbContext.Competitions
            .AsNoTracking()
            .Where(competition => competition.OrganizationId == organizationId)
            .OrderByDescending(competition => competition.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. competitions.Select(competition => new CompetitionSummaryView(
            competition.Id,
            competition.Name,
            competition.Season,
            competition.Modality.ToString(),
            competition.Status.ToString(),
            competition.UpdatedAt))];
    }

    public async Task<IReadOnlyList<OrganizerCompetitionView>> ListMineAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

        var memberships = await (
            from member in dbContext.OrganizationMembers.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on member.OrganizationId equals organization.Id
            where member.UserId == userId
            select new { organization.Id, organization.Name, member.Role })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var organizationIds = memberships.Select(membership => membership.Id).ToList();

        var competitions = await dbContext.Competitions
            .AsNoTracking()
            .Where(competition => organizationIds.Contains(competition.OrganizationId))
            .OrderByDescending(competition => competition.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var competitionIds = competitions.Select(competition => competition.Id).ToList();

        var teams = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => competitionIds.Contains(team.CompetitionId) && team.ArchivedAt == null)
            .GroupBy(team => team.CompetitionId)
            .Select(group => new { CompetitionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.CompetitionId, group => group.Count, cancellationToken)
            .ConfigureAwait(false);
        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => competitionIds.Contains(round.CompetitionId))
            .OrderBy(round => round.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var matches = await dbContext.Matches
            .AsNoTracking()
            .Where(match => competitionIds.Contains(match.CompetitionId) && match.Status == MatchStatus.Scheduled)
            .GroupBy(match => match.RoundId)
            .Select(group => new ScheduledMatches(
                group.Key,
                group.Count(),
                group.Min(match => match.KickoffAt)))
            .ToDictionaryAsync(group => group.RoundId, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var byOrganization = memberships.ToDictionary(membership => membership.Id);
        return [.. competitions.Select(competition =>
        {
            var own = rounds.Where(round => round.CompetitionId == competition.Id).ToList();
            var current = own.FirstOrDefault(round => InPlay.Contains(round.PhaseAt(now)));
            var next = own.FirstOrDefault(
                round => round.Status == RoundStatus.Draft && matches.ContainsKey(round.Id));
            var organization = byOrganization[competition.OrganizationId];

            return new OrganizerCompetitionView(
                competition.Id,
                competition.Name,
                competition.Season,
                competition.Modality.ToString(),
                competition.Status.ToString(),
                organization.Id,
                organization.Name,
                organization.Role.ToString(),
                competition.UpdatedAt,
                teams.GetValueOrDefault(competition.Id),
                own.Sum(round => matches.GetValueOrDefault(round.Id)?.Count ?? 0),
                RoundOf(current, competition.TimeZoneId, matches, now),
                RoundOf(next, competition.TimeZoneId, matches, now));
        })];
    }

    private static OrganizerRoundView? RoundOf(
        Round? round,
        string timeZoneId,
        Dictionary<Guid, ScheduledMatches> matches,
        DateTimeOffset now)
    {
        if (round is null)
        {
            return null;
        }

        var scheduled = matches.GetValueOrDefault(round.Id);
        return new OrganizerRoundView(
            round.Id,
            round.Name,
            round.PhaseAt(now).ToString(),
            scheduled?.Count ?? 0,
            scheduled is null ? null : CompetitionClock.ToLocalText(scheduled.First, timeZoneId),
            round.MarketCloseAt is { } closeAt ? CompetitionClock.ToLocalText(closeAt, timeZoneId) : null);
    }

    public async Task<CompetitionCommandResult> CreateDraftAsync(
        Guid organizationId,
        CompetitionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { Count: > 0 } errors)
        {
            return new CompetitionCommandResult(CompetitionCommandOutcome.Invalid, null, errors);
        }

        var userId = RequiredUserId();
        var now = clock.GetUtcNow();
        var competition = Competition.CreateDraft(Guid.CreateVersion7(), organizationId, userId, settings, now);
        dbContext.Competitions.Add(competition);
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            userId, "CompetitionDraftCreated", competition.Id, "Campeonato criado em rascunho.", now));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await CompletedAsync(competition.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompetitionDetailsView?> GetAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var userId = RequiredUserId();
        var found = await (
                from competition in dbContext.Competitions.AsNoTracking()
                join organization in dbContext.Organizations.AsNoTracking()
                    on competition.OrganizationId equals organization.Id
                join member in dbContext.OrganizationMembers.AsNoTracking()
                    on organization.Id equals member.OrganizationId
                where competition.Id == competitionId && member.UserId == userId
                select new { Competition = competition, OrganizationName = organization.Name, member.Role })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var item = found.Competition;
        var window = await CatalogAvailability.RegistrationWindowAsync(dbContext, item, cancellationToken)
            .ConfigureAwait(false);
        return new CompetitionDetailsView(
            item.Id,
            item.OrganizationId,
            found.OrganizationName,
            found.Role.ToString(),
            item.Name,
            item.Season,
            item.Modality.ToString(),
            item.Status.ToString(),
            item.TimeZoneId,
            (int)item.MarketCloseLeadTime.TotalMinutes,
            item.ResultsSlaBusinessDays,
            item.CorrectionWindowBusinessDays,
            item.RegistrationDeadline is { } deadline
                ? CompetitionClock.ToLocalText(deadline, item.TimeZoneId)
                : null,
            new RegistrationWindowView(
                window.ClosesAt is { } closesAt ? CompetitionClock.ToLocalText(closesAt, item.TimeZoneId) : null,
                window.Source.ToString(),
                window.RoundName,
                window.IsOpenAt(clock.GetUtcNow())),
            item.CanChangeModality,
            ModalityProfileView.From(item.ModalityProfile),
            item.CreatedAt,
            item.UpdatedAt,
            Convert.ToBase64String(item.RowVersion));
    }

    public async Task<CompetitionCommandResult> UpdateSettingsAsync(
        Guid competitionId,
        CompetitionSettings settings,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { Count: > 0 } errors)
        {
            return new CompetitionCommandResult(CompetitionCommandOutcome.Invalid, null, errors);
        }

        var competition = await dbContext.Competitions
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, competition.RowVersion, out var expectedVersion))
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.Conflict);
        }

        if (settings.Modality != competition.Modality && !competition.CanChangeModality)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.ModalityLocked);
        }

        var now = clock.GetUtcNow();
        competition.UpdateSettings(settings, now);

        // A comparação acima cobre quem leu antes; a versão original no UPDATE cobre quem
        // salvou entre a leitura e esta gravação.
        dbContext.Entry(competition).Property(item => item.RowVersion).OriginalValue = expectedVersion;

        // Salvar os mesmos valores ainda precisa passar pela checagem de versão no UPDATE.
        dbContext.Entry(competition).Property(item => item.UpdatedAt).IsModified = true;
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(),
            "CompetitionSettingsUpdated",
            competition.Id,
            "Configuração do campeonato alterada.",
            now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.Conflict);
        }

        return await CompletedAsync(competition.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CompetitionCommandResult> CompletedAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(competitionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O campeonato gravado não pôde ser lido de volta.");
        return new CompetitionCommandResult(CompetitionCommandOutcome.Completed, view, []);
    }

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}

/// <summary>Partidas marcadas de uma rodada: quantas e quando começa a primeira.</summary>
internal sealed record ScheduledMatches(Guid RoundId, int Count, DateTimeOffset First);
