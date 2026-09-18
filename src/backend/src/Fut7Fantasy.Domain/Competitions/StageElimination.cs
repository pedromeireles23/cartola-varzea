namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Quem já caiu no campeonato (01 §9, decisão de 2026-09-18).
///
/// Não existe chaveamento automático: o organizador confirma à mão quem joga cada fase.
/// Por isso a eliminação sai dessa confirmação. A fase que vale é a mais adiantada que já
/// tem participantes; time que jogou alguma fase anterior e não foi confirmado nela está
/// eliminado. Time que nunca entrou em fase nenhuma não está eliminado, só ainda não
/// foi escalado para jogar.
/// </summary>
public static class StageElimination
{
    public static IReadOnlySet<Guid> EliminatedTeams(
        IEnumerable<Stage> stages,
        IEnumerable<StageParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(participants);

        var teamsByStage = participants
            .GroupBy(participant => participant.StageId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.RealTeamId).ToHashSet());
        var confirmed = stages
            .Where(stage => teamsByStage.ContainsKey(stage.Id))
            .OrderBy(stage => stage.Sequence)
            .ToList();
        if (confirmed.Count < 2)
        {
            return new HashSet<Guid>();
        }

        var current = teamsByStage[confirmed[^1].Id];
        return confirmed[..^1]
            .SelectMany(stage => teamsByStage[stage.Id])
            .Where(team => !current.Contains(team))
            .ToHashSet();
    }
}
