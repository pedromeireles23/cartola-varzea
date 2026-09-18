using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.SportsCatalog;

/// <summary>Consultas de disponibilidade do catálogo que dependem das fases do campeonato.</summary>
internal static class CatalogAvailability
{
    /// <summary>Times eliminados, pela regra de <see cref="StageElimination"/>.</summary>
    public static async Task<IReadOnlySet<Guid>> EliminatedTeamsAsync(
        Fut7FantasyDbContext dbContext,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(stage => stage.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stageIds = stages.Select(stage => stage.Id).ToList();
        var participants = await dbContext.StageParticipants
            .AsNoTracking()
            .Where(participant => stageIds.Contains(participant.StageId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return StageElimination.EliminatedTeams(stages, participants);
    }

    /// <summary>
    /// Quantos times seguem no campeonato, que é de onde sai o limite por time real (01 §9):
    /// os da fase mais adiantada já confirmada, ou todos os não arquivados enquanto nenhuma
    /// fase tem times.
    /// </summary>
    public static async Task<int> ActiveRealTeamsAsync(
        Fut7FantasyDbContext dbContext,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var activeTeams = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.ArchivedAt == null)
            .Select(team => team.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latestStage = await (
                from stage in dbContext.Stages.AsNoTracking()
                where stage.CompetitionId == competitionId
                    && dbContext.StageParticipants.Any(participant => participant.StageId == stage.Id)
                orderby stage.Sequence descending
                select (Guid?)stage.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (latestStage is null)
        {
            return activeTeams.Count;
        }

        return await dbContext.StageParticipants
            .AsNoTracking()
            .CountAsync(
                participant => participant.StageId == latestStage && activeTeams.Contains(participant.RealTeamId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Prazo de inscrição de atletas que vale agora, pela regra de <see cref="RegistrationWindow"/>.</summary>
    public static async Task<RegistrationWindow> RegistrationWindowAsync(
        Fut7FantasyDbContext dbContext,
        Competition competition,
        CancellationToken cancellationToken)
    {
        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(stage => stage.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var matches = await dbContext.Matches
            .AsNoTracking()
            .Where(match => match.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return RegistrationWindow.For(competition, stages, rounds, matches);
    }
}
