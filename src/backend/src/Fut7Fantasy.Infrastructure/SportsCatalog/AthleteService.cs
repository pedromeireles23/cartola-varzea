using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.SportsCatalog;

public sealed class AthleteService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IAthleteService
{
    public async Task<IReadOnlyList<AthleteView>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(competitionId, cancellationToken).ConfigureAwait(false);
        var rows = await Rows(competitionId, null, activeOnly: false)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var eliminated = await CatalogAvailability.EliminatedTeamsAsync(dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. rows.Select(row => AthleteView.From(
                row.Athlete, row.Registration, row.Team, profile, eliminated.Contains(row.Team.Id))),
        ];
    }

    public Task<AthleteCommandResult> CreateAsync(
        Guid competitionId,
        Guid realTeamId,
        AthleteDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return Task.FromResult(new AthleteCommandResult(AthleteCommandOutcome.Invalid, null, errors));
        }

        return InCompetitionLockAsync(competitionId, async competition =>
        {
            var team = await dbContext.RealTeams
                .SingleOrDefaultAsync(
                    item => item.CompetitionId == competitionId
                        && item.Id == realTeamId
                        && item.ArchivedAt == null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (team is null)
            {
                return AthleteCommandResult.Of(AthleteCommandOutcome.TeamUnavailable);
            }

            var window = await CatalogAvailability.RegistrationWindowAsync(dbContext, competition, cancellationToken)
                .ConfigureAwait(false);
            if (!window.IsOpenAt(clock.GetUtcNow()))
            {
                return AthleteCommandResult.Of(AthleteCommandOutcome.RegistrationClosed);
            }

            var normalized = definition.Normalized();
            if (await NameExistsAsync(competitionId, normalized.SportingName, null, cancellationToken)
                .ConfigureAwait(false))
            {
                return AthleteCommandResult.Of(AthleteCommandOutcome.Duplicate);
            }

            var now = clock.GetUtcNow();
            var athlete = Athlete.Create(Guid.CreateVersion7(), competitionId, normalized, now);
            var registration = RosterRegistration.Create(
                Guid.CreateVersion7(), competitionId, athlete.Id, realTeamId, normalized, now);
            dbContext.Athletes.Add(athlete);
            dbContext.RosterRegistrations.Add(registration);
            AddAudit("AthleteCreated", athlete.Id, "Atleta cadastrado e inscrito no time.", now);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var eliminated = await CatalogAvailability.EliminatedTeamsAsync(
                    dbContext, competitionId, cancellationToken)
                .ConfigureAwait(false);
            return new AthleteCommandResult(
                AthleteCommandOutcome.Completed,
                AthleteView.From(
                    athlete, registration, team, competition.ModalityProfile, eliminated.Contains(team.Id)),
                []);
        }, cancellationToken);
    }

    public async Task<AthleteCommandResult> UpdateAsync(
        Guid competitionId,
        Guid athleteId,
        Guid realTeamId,
        AthleteDefinition definition,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return new AthleteCommandResult(AthleteCommandOutcome.Invalid, null, errors);
        }

        var row = await Rows(competitionId, athleteId, activeOnly: true)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.NotFound);
        }

        if (row.Registration.RealTeamId != realTeamId)
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.TransferNotAllowed);
        }

        if (!RowVersions.Matches(version, row.Athlete.RowVersion, out var expectedVersion))
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.Conflict);
        }

        var normalized = definition.Normalized();
        if (await NameExistsAsync(competitionId, normalized.SportingName, athleteId, cancellationToken)
            .ConfigureAwait(false))
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.Duplicate);
        }

        var now = clock.GetUtcNow();
        try
        {
            row.Athlete.Update(normalized, now);
        }
        catch (InvalidOperationException)
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.PositionLocked);
        }

        row.Registration.UpdatePricing(normalized);
        dbContext.Entry(row.Athlete).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.Entry(row.Athlete).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit("AthleteUpdated", row.Athlete.Id, "Cadastro do atleta alterado.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.Conflict);
        }
        catch (DbUpdateException)
        {
            return AthleteCommandResult.Of(AthleteCommandOutcome.Duplicate);
        }

        var profile = await ProfileAsync(competitionId, cancellationToken).ConfigureAwait(false);
        var eliminated = await CatalogAvailability.EliminatedTeamsAsync(dbContext, competitionId, cancellationToken)
            .ConfigureAwait(false);
        return new AthleteCommandResult(
            AthleteCommandOutcome.Completed,
            AthleteView.From(row.Athlete, row.Registration, row.Team, profile, eliminated.Contains(row.Team.Id)),
            []);
    }

    public async Task<AthleteCommandOutcome> ReleaseAsync(
        Guid competitionId,
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var row = await Rows(competitionId, athleteId, activeOnly: true)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return AthleteCommandOutcome.NotFound;
        }

        var now = clock.GetUtcNow();
        row.Registration.Release(now);
        row.Athlete.Touch(now);
        dbContext.Entry(row.Athlete).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit("AthleteReleased", row.Athlete.Id, "Atleta desligado do time.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AthleteCommandOutcome.NotFound;
        }

        return AthleteCommandOutcome.Completed;
    }

    private IQueryable<AthleteRow> Rows(Guid competitionId, Guid? athleteId, bool activeOnly) =>
        from athlete in dbContext.Athletes
        join registration in dbContext.RosterRegistrations on athlete.Id equals registration.AthleteId
        join team in dbContext.RealTeams on registration.RealTeamId equals team.Id
        where athlete.CompetitionId == competitionId
            && registration.CompetitionId == competitionId
            && team.CompetitionId == competitionId
            && (athleteId == null || athlete.Id == athleteId)
            && (!activeOnly || registration.Status == RosterRegistrationStatus.Active)
        orderby registration.ReleasedAt != null, team.Name, athlete.SportingName
        select new AthleteRow(athlete, registration, team);

    private Task<bool> NameExistsAsync(
        Guid competitionId,
        string sportingName,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Athletes.AnyAsync(
            athlete => athlete.CompetitionId == competitionId
                && athlete.SportingName == sportingName
                && athlete.Id != exceptId,
            cancellationToken);

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

    private Task<T> InCompetitionLockAsync<T>(
        Guid competitionId,
        Func<Competition, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            var competition = await dbContext.Competitions
                .FromSql($"""
                    SELECT * FROM [competitions].[Competitions] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {competitionId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await operation(competition).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }

    private void AddAudit(string action, Guid targetId, string reason, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(), action, targetId, reason, occurredAt));

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

    private sealed record AthleteRow(
        Athlete Athlete,
        RosterRegistration Registration,
        RealTeam Team);
}
