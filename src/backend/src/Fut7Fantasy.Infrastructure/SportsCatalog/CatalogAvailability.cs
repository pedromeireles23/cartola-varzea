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
}
