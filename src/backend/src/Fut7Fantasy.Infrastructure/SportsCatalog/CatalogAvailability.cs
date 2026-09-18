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
