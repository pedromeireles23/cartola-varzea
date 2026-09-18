using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Casos de uso do campeonato na área de organização. As policies já validaram o
/// escopo: organização para listar e criar, campeonato para ler e editar.
/// </summary>
public interface ICompetitionManagementService
{
    Task<IReadOnlyList<CompetitionSummaryView>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<CompetitionCommandResult> CreateDraftAsync(
        Guid organizationId,
        CompetitionSettings settings,
        CancellationToken cancellationToken);

    Task<CompetitionDetailsView?> GetAsync(Guid competitionId, CancellationToken cancellationToken);

    /// <summary>
    /// Substitui a configuração. <paramref name="version"/> é a versão lida pela tela:
    /// se alguém salvou antes, o resultado é conflito e nada é gravado.
    /// </summary>
    Task<CompetitionCommandResult> UpdateSettingsAsync(
        Guid competitionId,
        CompetitionSettings settings,
        string version,
        CancellationToken cancellationToken);
}

public enum CompetitionCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    ModalityLocked,
    Conflict,
}

public sealed record CompetitionCommandResult(
    CompetitionCommandOutcome Outcome,
    CompetitionDetailsView? Competition,
    IReadOnlyList<CompetitionSettingsError> Errors)
{
    public static CompetitionCommandResult Of(CompetitionCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record CompetitionSummaryView(
    Guid Id,
    string Name,
    string Season,
    string Modality,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Configuração completa do campeonato. <c>ViewerRole</c> é o papel da conta atual na
/// organização, para a interface decidir o que oferecer; <c>Version</c> é o token de
/// concorrência que a edição precisa devolver.
/// </summary>
public sealed record CompetitionDetailsView(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string ViewerRole,
    string Name,
    string Season,
    string Modality,
    string Status,
    string TimeZoneId,
    int MarketCloseLeadTimeMinutes,
    int ResultsSlaBusinessDays,
    int CorrectionWindowBusinessDays,
    string? RegistrationDeadlineLocal,
    RegistrationWindowView RegistrationWindow,
    bool CanChangeModality,
    ModalityProfileView ModalityProfile,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version);

/// <summary>
/// O prazo de inscrição que vale agora, já escrito no fuso do campeonato.
/// </summary>
/// <param name="ClosesAtLocal">Fechamento, ou nulo enquanto ainda não há data.</param>
/// <param name="Source">`Configured`, `FirstStageLastRound` ou `NotYetDefined`.</param>
/// <param name="RoundName">Rodada que define o prazo padrão.</param>
/// <param name="IsOpen">Se um atleta novo ainda pode ser inscrito agora.</param>
public sealed record RegistrationWindowView(
    string? ClosesAtLocal,
    string Source,
    string? RoundName,
    bool IsOpen);
