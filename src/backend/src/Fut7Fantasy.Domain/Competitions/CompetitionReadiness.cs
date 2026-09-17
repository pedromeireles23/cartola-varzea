using System.Collections.Frozen;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Peso de um item do checklist de publicação.</summary>
public enum CompetitionReadinessSeverity
{
    /// <summary>Deixa publicar, mas indica que a demonstração sairá pior do que poderia.</summary>
    Warning = 1,

    /// <summary>Impede a publicação: o campeonato não teria como funcionar.</summary>
    Blocker = 2,
}

/// <summary>
/// Um item do checklist. <see cref="Code"/> é estável e serve para teste e interface;
/// <see cref="Message"/> é o texto que o organizador lê.
/// </summary>
public sealed record CompetitionReadinessItem(
    string Code,
    CompetitionReadinessSeverity Severity,
    string Message);

/// <summary>Fase pesada pelo checklist: interessa o nome e quantos times foram confirmados.</summary>
public sealed record StageReadiness(string Name, int ParticipantCount);

/// <summary>Time real ativo e quantos atletas seguem inscritos nele.</summary>
public sealed record RealTeamReadiness(string Name, int ActiveAthletes);

/// <summary>
/// Retrato do catálogo no instante da avaliação. O checklist é uma regra de domínio,
/// então recebe números prontos em vez de consultar o banco.
/// </summary>
/// <param name="Stages">Fases na ordem do campeonato.</param>
/// <param name="ActiveTeams">Times não arquivados.</param>
/// <param name="AvailableAthletesByPosition">Atletas disponíveis por posição.</param>
/// <param name="AssetsShareOnePriceLevel">
/// Verdadeiro quando todo ativo do mercado tem o mesmo preço: nesse caso o orçamento
/// não força escolha nenhuma (01 §9).
/// </param>
public sealed record CompetitionReadinessSnapshot(
    IReadOnlyList<StageReadiness> Stages,
    IReadOnlyList<RealTeamReadiness> ActiveTeams,
    IReadOnlyDictionary<Position, int> AvailableAthletesByPosition,
    bool AssetsShareOnePriceLevel);

/// <summary>Resultado do checklist, com os impedimentos antes dos alertas.</summary>
public sealed record CompetitionReadinessReport(IReadOnlyList<CompetitionReadinessItem> Items)
{
    public bool CanPublish =>
        !Items.Any(item => item.Severity == CompetitionReadinessSeverity.Blocker);
}

/// <summary>
/// Checklist de prontidão para publicar (01 §7 e §9). Ele responde uma pergunta só:
/// alguém que abrisse este campeonato hoje conseguiria montar uma equipe válida?
/// </summary>
public static class CompetitionReadiness
{
    /// <summary>Uma rodada precisa de dois times reais; abaixo disso não existe partida.</summary>
    public const int MinimumRealTeams = 2;

    public const string NoStagesCode = "no_stages";
    public const string NotEnoughTeamsCode = "not_enough_teams";
    public const string NotEnoughAthletesCode = "not_enough_athletes";
    public const string NoStageParticipantsCode = "no_stage_participants";
    public const string StageWithoutParticipantsCode = "stage_without_participants";
    public const string ThinRealTeamRosterCode = "thin_real_team_roster";
    public const string SinglePriceLevelCode = "single_price_level";

    private static readonly FrozenDictionary<Position, string> PositionNames =
        new Dictionary<Position, string>
        {
            [Position.Goalkeeper] = "goleiro",
            [Position.Defender] = "defensor",
            [Position.Midfielder] = "meio-campista",
            [Position.Forward] = "atacante",
        }.ToFrozenDictionary();

    public static CompetitionReadinessReport Evaluate(
        ModalityProfile profile,
        CompetitionReadinessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        List<CompetitionReadinessItem> blockers = [];
        List<CompetitionReadinessItem> warnings = [];

        if (snapshot.Stages.Count == 0)
        {
            blockers.Add(new(
                NoStagesCode,
                CompetitionReadinessSeverity.Blocker,
                "Crie ao menos uma fase: as rodadas acontecem dentro de uma fase."));
        }

        if (snapshot.ActiveTeams.Count < MinimumRealTeams)
        {
            blockers.Add(new(
                NotEnoughTeamsCode,
                CompetitionReadinessSeverity.Blocker,
                $"Cadastre ao menos {MinimumRealTeams} times ativos; uma partida precisa de dois."));
        }

        // Um elenco válido tem os titulares da formação e um reserva por posição.
        foreach (var position in Enum.GetValues<Position>())
        {
            var required = profile.Formation.CountOf(position) + ModalityProfile.BenchPerPosition;
            var available = snapshot.AvailableAthletesByPosition.TryGetValue(position, out var count)
                ? count
                : 0;
            if (available < required)
            {
                blockers.Add(new(
                    NotEnoughAthletesCode,
                    CompetitionReadinessSeverity.Blocker,
                    $"Faltam atletas na posição {PositionNames[position]}: "
                    + $"o elenco pede {required} e o campeonato tem {available}."));
            }
        }

        if (snapshot.Stages.Count > 0 && snapshot.Stages.All(stage => stage.ParticipantCount == 0))
        {
            blockers.Add(new(
                NoStageParticipantsCode,
                CompetitionReadinessSeverity.Blocker,
                "Confirme os times de pelo menos uma fase antes de publicar."));
        }
        else
        {
            foreach (var stage in snapshot.Stages.Where(stage => stage.ParticipantCount == 0))
            {
                warnings.Add(new(
                    StageWithoutParticipantsCode,
                    CompetitionReadinessSeverity.Warning,
                    $"A fase {stage.Name} ainda não tem times confirmados."));
            }
        }

        foreach (var team in snapshot.ActiveTeams
            .Where(team => team.ActiveAthletes < profile.MinimumAthletesPerRealTeam))
        {
            warnings.Add(new(
                ThinRealTeamRosterCode,
                CompetitionReadinessSeverity.Warning,
                $"{team.Name} tem {team.ActiveAthletes} "
                + $"{(team.ActiveAthletes == 1 ? "atleta inscrito" : "atletas inscritos")}; "
                + $"a modalidade pede ao menos {profile.MinimumAthletesPerRealTeam}."));
        }

        if (snapshot.AssetsShareOnePriceLevel)
        {
            warnings.Add(new(
                SinglePriceLevelCode,
                CompetitionReadinessSeverity.Warning,
                "Todos os ativos custam o mesmo: qualquer escalação caberia no orçamento. "
                + "Use os níveis de preço para diferenciar os destaques."));
        }

        return new CompetitionReadinessReport([.. blockers, .. warnings]);
    }
}
