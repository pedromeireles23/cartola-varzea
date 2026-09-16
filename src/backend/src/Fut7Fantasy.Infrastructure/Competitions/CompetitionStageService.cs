using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class CompetitionStageService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICompetitionStageService
{
    private const string GroupsNavigation = "_groups";

    public async Task<IReadOnlyList<StageView>> ListAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var stages = await StagesOf(competitionId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. stages.Select(StageView.From)];
    }

    public Task<StageCommandResult> CreateAsync(
        Guid competitionId,
        StageDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return Task.FromResult(new StageCommandResult(StageCommandOutcome.Invalid, null, errors));
        }

        return InCompetitionLockAsync(competitionId, async () =>
        {
            var count = await dbContext.Stages
                .CountAsync(stage => stage.CompetitionId == competitionId, cancellationToken)
                .ConfigureAwait(false);
            if (count >= StageDefinition.MaxStagesPerCompetition)
            {
                return StageCommandResult.Of(StageCommandOutcome.LimitReached);
            }

            var now = clock.GetUtcNow();
            var stage = Stage.Create(Guid.CreateVersion7(), competitionId, count + 1, definition, now);
            dbContext.Stages.Add(stage);
            AddAudit("CompetitionStageCreated", stage.Id, "Fase criada.", now);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new StageCommandResult(StageCommandOutcome.Completed, StageView.From(stage), []);
        }, cancellationToken);
    }

    public async Task<StageCommandResult> UpdateAsync(
        Guid competitionId,
        Guid stageId,
        StageDefinition definition,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return new StageCommandResult(StageCommandOutcome.Invalid, null, errors);
        }

        var stage = await StagesOf(competitionId)
            .SingleOrDefaultAsync(item => item.Id == stageId, cancellationToken)
            .ConfigureAwait(false);
        if (stage is null)
        {
            return StageCommandResult.Of(StageCommandOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, stage.RowVersion, out var expectedVersion))
        {
            return StageCommandResult.Of(StageCommandOutcome.Conflict);
        }

        if (!stage.KnowsAllGroupsOf(definition))
        {
            return new StageCommandResult(
                StageCommandOutcome.Invalid,
                null,
                [new(nameof(StageDefinition.Groups), "Um dos grupos não pertence a esta fase.")]);
        }

        var now = clock.GetUtcNow();
        stage.Update(definition, now);
        dbContext.Entry(stage).Property(item => item.RowVersion).OriginalValue = expectedVersion;

        // Mudar só os grupos não altera a linha da fase; forçar o UPDATE mantém a checagem
        // de versão e faz a versão avançar.
        dbContext.Entry(stage).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit("CompetitionStageUpdated", stage.Id, "Fase alterada.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StageCommandResult.Of(StageCommandOutcome.Conflict);
        }

        return new StageCommandResult(StageCommandOutcome.Completed, StageView.From(stage), []);
    }

    public Task<StageCommandOutcome> DeleteAsync(
        Guid competitionId,
        Guid stageId,
        CancellationToken cancellationToken) =>
        InCompetitionLockAsync(competitionId, async () =>
        {
            var stages = await StagesOf(competitionId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var removed = stages.SingleOrDefault(stage => stage.Id == stageId);
            if (removed is null)
            {
                return StageCommandOutcome.NotFound;
            }

            dbContext.Stages.Remove(removed);
            var sequence = 1;
            foreach (var stage in stages.Where(stage => stage.Id != stageId))
            {
                stage.MoveTo(sequence++);
            }

            AddAudit("CompetitionStageDeleted", stageId, "Fase removida.", clock.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return StageCommandOutcome.Completed;
        }, cancellationToken);

    public Task<StageListResult> ReorderAsync(
        Guid competitionId,
        IReadOnlyList<Guid> stageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stageIds);

        return InCompetitionLockAsync(competitionId, async () =>
        {
            var stages = await StagesOf(competitionId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var sameStages = stageIds.Count == stages.Count
                && stageIds.Distinct().Count() == stageIds.Count
                && stages.All(stage => stageIds.Contains(stage.Id));
            if (!sameStages)
            {
                return new StageListResult(StageCommandOutcome.Conflict, [.. stages.Select(StageView.From)]);
            }

            foreach (var stage in stages)
            {
                stage.MoveTo(IndexOf(stageIds, stage.Id) + 1);
            }

            AddAudit("CompetitionStagesReordered", competitionId, "Ordem das fases alterada.", clock.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new StageListResult(
                StageCommandOutcome.Completed,
                [.. stages.OrderBy(stage => stage.Sequence).Select(StageView.From)]);
        }, cancellationToken);
    }

    private IQueryable<Stage> StagesOf(Guid competitionId) =>
        dbContext.Stages
            .Include(GroupsNavigation)
            .Where(stage => stage.CompetitionId == competitionId)
            .OrderBy(stage => stage.Sequence);

    /// <summary>
    /// Serializa as operações que mexem na ordem das fases de um mesmo campeonato. A trava
    /// fica na linha do campeonato, como na aprovação de organizadores: read committed com
    /// UPDLOCK, sem range locks que travariam campeonatos vizinhos.
    /// </summary>
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

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            if (ids[index] == id)
            {
                return index;
            }
        }

        throw new InvalidOperationException("A fase não está na lista.");
    }

    private void AddAudit(string action, Guid targetId, string reason, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            action,
            targetId,
            reason,
            occurredAt));
}
