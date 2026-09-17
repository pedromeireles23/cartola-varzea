using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.SportsCatalog;

public sealed class RealTeamService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IRealTeamService
{
    public async Task<IReadOnlyList<RealTeamView>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => team.CompetitionId == competitionId)
            .OrderBy(team => team.ArchivedAt != null)
            .ThenBy(team => team.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. teams.Select(RealTeamView.From)];
    }

    public Task<RealTeamCommandResult> CreateAsync(
        Guid competitionId,
        RealTeamDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return Task.FromResult(new RealTeamCommandResult(RealTeamCommandOutcome.Invalid, null, errors));
        }

        return InCompetitionLockAsync(competitionId, async () =>
        {
            var normalized = definition.Normalized();
            if (await NameExistsAsync(competitionId, normalized.Name, null, cancellationToken).ConfigureAwait(false))
            {
                return RealTeamCommandResult.Of(RealTeamCommandOutcome.Duplicate);
            }

            var now = clock.GetUtcNow();
            var team = RealTeam.Create(Guid.CreateVersion7(), competitionId, normalized, now);
            var coach = Coach.Create(
                Guid.CreateVersion7(),
                competitionId,
                team.Id,
                new CoachDefinition(null, PriceTier.Regular, null),
                now);
            dbContext.RealTeams.Add(team);
            dbContext.Coaches.Add(coach);
            AddAudit("RealTeamCreated", team.Id, "Time real cadastrado.", now);
            AddAudit("CoachCreated", coach.Id, "Ativo de técnico criado com o time.", now);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new RealTeamCommandResult(RealTeamCommandOutcome.Completed, RealTeamView.From(team), []);
        }, cancellationToken);
    }

    public async Task<RealTeamCommandResult> UpdateAsync(
        Guid competitionId,
        Guid teamId,
        RealTeamDefinition definition,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return new RealTeamCommandResult(RealTeamCommandOutcome.Invalid, null, errors);
        }

        var team = await dbContext.RealTeams
            .SingleOrDefaultAsync(
                item => item.CompetitionId == competitionId && item.Id == teamId && item.ArchivedAt == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return RealTeamCommandResult.Of(RealTeamCommandOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, team.RowVersion, out var expectedVersion))
        {
            return RealTeamCommandResult.Of(RealTeamCommandOutcome.Conflict);
        }

        var normalized = definition.Normalized();
        if (await NameExistsAsync(competitionId, normalized.Name, teamId, cancellationToken).ConfigureAwait(false))
        {
            return RealTeamCommandResult.Of(RealTeamCommandOutcome.Duplicate);
        }

        var now = clock.GetUtcNow();
        team.Update(normalized, now);
        dbContext.Entry(team).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.Entry(team).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit("RealTeamUpdated", team.Id, "Time real alterado.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RealTeamCommandResult.Of(RealTeamCommandOutcome.Conflict);
        }
        catch (DbUpdateException)
        {
            return RealTeamCommandResult.Of(RealTeamCommandOutcome.Duplicate);
        }

        return new RealTeamCommandResult(RealTeamCommandOutcome.Completed, RealTeamView.From(team), []);
    }

    public async Task<RealTeamCommandOutcome> ArchiveAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var team = await dbContext.RealTeams
            .SingleOrDefaultAsync(
                item => item.CompetitionId == competitionId && item.Id == teamId && item.ArchivedAt == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return RealTeamCommandOutcome.NotFound;
        }

        var now = clock.GetUtcNow();
        team.Archive(now);
        AddAudit("RealTeamArchived", team.Id, "Time real arquivado.", now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RealTeamCommandOutcome.NotFound;
        }

        return RealTeamCommandOutcome.Completed;
    }

    private Task<bool> NameExistsAsync(
        Guid competitionId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.RealTeams.AnyAsync(
            team => team.CompetitionId == competitionId && team.Name == name && team.Id != exceptId,
            cancellationToken);

    private Task<T> InCompetitionLockAsync<T>(
        Guid competitionId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            await dbContext.Competitions
                .FromSql($"""
                    SELECT * FROM [competitions].[Competitions] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {competitionId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await operation().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }

    private void AddAudit(string action, Guid targetId, string reason, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(), action, targetId, reason, occurredAt));

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
