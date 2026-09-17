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
        return await ViewsAsync(stages, cancellationToken).ConfigureAwait(false);
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

        var participants = await dbContext.StageParticipants
            .AsNoTracking()
            .Where(participant => participant.StageId == stageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var changesFormatWithParticipants = participants.Count > 0 && stage.Format != definition.Format;
        var keptGroupIds = definition.Groups
            .Where(group => group.Id is not null)
            .Select(group => group.Id!.Value)
            .ToHashSet();
        var removesUsedGroup = participants.Any(
            participant => participant.StageGroupId is { } groupId && !keptGroupIds.Contains(groupId));

        // Com partida marcada, trocar o formato mudaria o significado de um jogo que já
        // tem data: o organizador precisa desmarcar a partida primeiro.
        var hasMatches = await dbContext.Matches
            .AnyAsync(match => match.StageId == stageId, cancellationToken)
            .ConfigureAwait(false);
        if (changesFormatWithParticipants || removesUsedGroup || (hasMatches && stage.Format != definition.Format))
        {
            return StageCommandResult.Of(StageCommandOutcome.DependenciesExist);
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

        return new StageCommandResult(
            StageCommandOutcome.Completed,
            await ViewAsync(stage, cancellationToken).ConfigureAwait(false),
            []);
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

            // Remover a fase leva junto as associações de time, mas nunca partidas: uma
            // partida marcada é um compromisso com hora, e sumir com ela em silêncio
            // seria pior do que exigir que o organizador a remova antes.
            if (await dbContext.Matches
                .AnyAsync(match => match.StageId == stageId, cancellationToken)
                .ConfigureAwait(false))
            {
                return StageCommandOutcome.DependenciesExist;
            }

            var participants = await dbContext.StageParticipants
                .Where(participant => participant.StageId == stageId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            dbContext.StageParticipants.RemoveRange(participants);
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
                return new StageListResult(
                    StageCommandOutcome.Conflict,
                    await ViewsAsync(stages, cancellationToken).ConfigureAwait(false));
            }

            foreach (var stage in stages)
            {
                stage.MoveTo(IndexOf(stageIds, stage.Id) + 1);
            }

            AddAudit("CompetitionStagesReordered", competitionId, "Ordem das fases alterada.", clock.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new StageListResult(
                StageCommandOutcome.Completed,
                await ViewsAsync(
                    [.. stages.OrderBy(stage => stage.Sequence)],
                    cancellationToken).ConfigureAwait(false));
        }, cancellationToken);
    }

    public async Task<StageCommandResult> SetParticipantsAsync(
        Guid competitionId,
        Guid stageId,
        IReadOnlyList<StageParticipantAssignment> participants,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var duplicateTeams = participants
            .GroupBy(participant => participant.RealTeamId)
            .Any(group => group.Count() > 1);
        if (duplicateTeams || participants.Any(participant => participant.RealTeamId == Guid.Empty))
        {
            return InvalidParticipants("Cada time pode aparecer uma única vez na fase.");
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

        var current = await dbContext.StageParticipants
            .Where(participant => participant.StageId == stageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var requestedTeamIds = participants.Select(participant => participant.RealTeamId).ToHashSet();
        var teams = await dbContext.RealTeams
            .Where(team => team.CompetitionId == competitionId && requestedTeamIds.Contains(team.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentTeamIds = current.Select(participant => participant.RealTeamId).ToHashSet();
        if (teams.Count != requestedTeamIds.Count
            || teams.Any(team => team.IsArchived && !currentTeamIds.Contains(team.Id)))
        {
            return InvalidParticipants("Escolha apenas times ativos deste campeonato.");
        }

        var stageGroupIds = stage.Groups.Select(group => group.Id).ToHashSet();
        if (stage.Format == StageFormat.Groups
            && participants.Any(
                participant => participant.StageGroupId is not { } groupId
                    || !stageGroupIds.Contains(groupId)))
        {
            return InvalidParticipants("Escolha um grupo desta fase para cada time.");
        }

        if (stage.Format == StageFormat.Knockout
            && participants.Any(participant => participant.StageGroupId is not null))
        {
            return InvalidParticipants("Times do mata-mata não pertencem a grupos.");
        }

        var now = clock.GetUtcNow();
        dbContext.StageParticipants.RemoveRange(
            current.Where(participant => !requestedTeamIds.Contains(participant.RealTeamId)));
        foreach (var assignment in participants)
        {
            var existing = current.SingleOrDefault(
                participant => participant.RealTeamId == assignment.RealTeamId);
            if (existing is null)
            {
                dbContext.StageParticipants.Add(StageParticipant.Create(
                    Guid.CreateVersion7(),
                    stageId,
                    assignment.RealTeamId,
                    assignment.StageGroupId,
                    now));
            }
            else if (existing.StageGroupId != assignment.StageGroupId)
            {
                existing.AssignToGroup(assignment.StageGroupId, now);
            }
        }

        stage.MarkParticipantsChanged(now);
        dbContext.Entry(stage).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.Entry(stage).Property(item => item.UpdatedAt).IsModified = true;
        AddAudit(
            "CompetitionStageParticipantsUpdated",
            stage.Id,
            "Times participantes da fase alterados.",
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StageCommandResult.Of(StageCommandOutcome.Conflict);
        }

        return new StageCommandResult(
            StageCommandOutcome.Completed,
            await ViewAsync(stage, cancellationToken).ConfigureAwait(false),
            []);
    }

    private IQueryable<Stage> StagesOf(Guid competitionId) =>
        dbContext.Stages
            .Include(GroupsNavigation)
            .Where(stage => stage.CompetitionId == competitionId)
            .OrderBy(stage => stage.Sequence);

    private async Task<StageView> ViewAsync(Stage stage, CancellationToken cancellationToken) =>
        (await ViewsAsync([stage], cancellationToken).ConfigureAwait(false)).Single();

    private async Task<IReadOnlyList<StageView>> ViewsAsync(
        List<Stage> stages,
        CancellationToken cancellationToken)
    {
        if (stages.Count == 0)
        {
            return [];
        }

        var stageIds = stages.Select(stage => stage.Id).ToArray();
        var rows = await (
            from participant in dbContext.StageParticipants.AsNoTracking()
            join team in dbContext.RealTeams.AsNoTracking()
                on participant.RealTeamId equals team.Id
            join groupItem in dbContext.Set<StageGroup>().AsNoTracking()
                on participant.StageGroupId equals groupItem.Id into participantGroups
            from groupItem in participantGroups.DefaultIfEmpty()
            where stageIds.Contains(participant.StageId)
            select new
            {
                participant.Id,
                participant.StageId,
                participant.RealTeamId,
                RealTeamName = team.Name,
                team.ArchivedAt,
                participant.StageGroupId,
                StageGroupName = groupItem == null ? null : groupItem.Name,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. stages
                .OrderBy(stage => stage.Sequence)
                .Select(stage => StageView.From(
                    stage,
                    [.. rows
                        .Where(row => row.StageId == stage.Id)
                        .OrderBy(row => row.StageGroupName)
                        .ThenBy(row => row.RealTeamName)
                        .Select(row => new StageParticipantView(
                            row.Id,
                            row.RealTeamId,
                            row.RealTeamName,
                            row.ArchivedAt is not null,
                            row.StageGroupId,
                            row.StageGroupName))]))
        ];
    }

    private static StageCommandResult InvalidParticipants(string message) =>
        new(
            StageCommandOutcome.Invalid,
            null,
            [new("Participants", message)]);

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
