using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.SportsCatalog;

public sealed class CoachService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICoachService
{
    public async Task<IReadOnlyList<CoachView>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(competitionId, cancellationToken).ConfigureAwait(false);
        var rows = await Rows(competitionId, null, activeTeamOnly: false)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var eliminated = await CatalogAvailability.EliminatedTeamsAsync(dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(row => CoachView.From(row.Coach, row.Team, profile, eliminated.Contains(row.Team.Id)))];
    }

    public async Task<CoachCommandResult> UpdateAsync(
        Guid competitionId,
        Guid coachId,
        CoachDefinition definition,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return new CoachCommandResult(CoachCommandOutcome.Invalid, null, errors);
        }

        var row = await Rows(competitionId, coachId, activeTeamOnly: true)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return CoachCommandResult.Of(CoachCommandOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, row.Coach.RowVersion, out var expectedVersion))
        {
            return CoachCommandResult.Of(CoachCommandOutcome.Conflict);
        }

        var now = clock.GetUtcNow();
        row.Coach.Update(definition.Normalized(), now);
        dbContext.Entry(row.Coach).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.Entry(row.Coach).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit("CoachUpdated", row.Coach.Id, "Ativo de técnico alterado.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CoachCommandResult.Of(CoachCommandOutcome.Conflict);
        }

        var profile = await ProfileAsync(competitionId, cancellationToken).ConfigureAwait(false);
        var eliminated = await CatalogAvailability.EliminatedTeamsAsync(dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        return new CoachCommandResult(
            CoachCommandOutcome.Completed,
            CoachView.From(row.Coach, row.Team, profile, eliminated.Contains(row.Team.Id)),
            []);
    }

    private IQueryable<CoachRow> Rows(Guid competitionId, Guid? coachId, bool activeTeamOnly) =>
        from coach in dbContext.Coaches
        join team in dbContext.RealTeams on coach.RealTeamId equals team.Id
        where coach.CompetitionId == competitionId
            && team.CompetitionId == competitionId
            && (coachId == null || coach.Id == coachId)
            && (!activeTeamOnly || team.ArchivedAt == null)
        orderby team.ArchivedAt != null, team.Name
        select new CoachRow(coach, team);

    private async Task<ModalityProfile> ProfileAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .SingleAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        return competition.ModalityProfile;
    }

    private void AddAudit(string action, Guid targetId, string reason, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(), action, targetId, reason, occurredAt));

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

    private sealed record CoachRow(Coach Coach, RealTeam Team);
}
