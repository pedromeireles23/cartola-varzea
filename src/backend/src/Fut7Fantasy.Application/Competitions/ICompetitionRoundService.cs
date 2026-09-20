using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Rodadas e partidas do campeonato. A policy de campeonato já validou o escopo; toda
/// consulta ainda filtra pelo campeonato da rota.
/// </summary>
public interface ICompetitionRoundService
{
    Task<IReadOnlyList<RoundView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);

    Task<RoundReviewView?> ReviewAsync(
        Guid competitionId,
        Guid roundId,
        CancellationToken cancellationToken);

    /// <summary>Acrescenta a rodada ao fim da ordem, em rascunho.</summary>
    Task<RoundCommandResult> CreateAsync(
        Guid competitionId,
        RoundDefinition definition,
        CancellationToken cancellationToken);

    Task<RoundCommandResult> RenameAsync(
        Guid competitionId,
        Guid roundId,
        RoundDefinition definition,
        string version,
        CancellationToken cancellationToken);

    /// <summary>Remove uma rodada em rascunho, junto das partidas dela, e renumera as seguintes.</summary>
    Task<RoundCommandOutcome> DeleteAsync(Guid competitionId, Guid roundId, CancellationToken cancellationToken);

    /// <summary>
    /// Abre o mercado, volta para rascunho ou cancela. A transição é o que a tela pede;
    /// o domínio recusa o que não faz sentido a partir do estado atual.
    /// </summary>
    Task<RoundCommandResult> ChangeStatusAsync(
        Guid competitionId,
        Guid roundId,
        RoundTransition transition,
        string version,
        CancellationToken cancellationToken);

    Task<RoundCommandResult> AddMatchAsync(
        Guid competitionId,
        Guid roundId,
        MatchInput match,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Altera a partida. Com a rodada em rascunho troca tudo; com o mercado aberto, só o
    /// horário e a situação, que é o reagendamento, o adiamento e o cancelamento.
    /// </summary>
    Task<RoundCommandResult> UpdateMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        MatchInput match,
        string version,
        CancellationToken cancellationToken);

    Task<RoundCommandResult> RemoveMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        string version,
        CancellationToken cancellationToken);
}

/// <summary>O que a tela pede; o nome diz a intenção, não o estado de destino.</summary>
public enum RoundTransition
{
    OpenMarket,
    ReopenForEditing,
    SendToReview,
    Cancel,
}

public enum RoundCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,
    LimitReached,

    /// <summary>A rodada está num estado que não aceita essa operação.</summary>
    StatusLocked,
}

public sealed record RoundCommandResult(
    RoundCommandOutcome Outcome,
    RoundView? Round,
    IReadOnlyList<RoundError> Errors)
{
    public static RoundCommandResult Of(RoundCommandOutcome outcome) => new(outcome, null, []);

    public static RoundCommandResult Invalid(params RoundError[] errors) =>
        new(RoundCommandOutcome.Invalid, null, errors);
}

/// <summary>
/// Partida como a tela envia. O horário chega como texto local (`2026-09-20T15:30`) e o
/// servidor converte pelo fuso do campeonato.
/// </summary>
public sealed record MatchInput(
    Guid StageId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    string? KickoffLocal,
    string? Status);

public sealed record MatchView(
    Guid Id,
    Guid StageId,
    string StageName,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid AwayTeamId,
    string AwayTeamName,
    string? GroupName,
    DateTimeOffset KickoffAt,
    string KickoffLocal,
    string Status);

/// <summary>Rodada com as partidas em ordem de horário e a versão que a edição devolve.</summary>
public sealed record RoundView(
    Guid Id,
    string Name,
    int Sequence,
    string Status,
    string Phase,
    DateTimeOffset? MarketCloseAt,
    string? MarketCloseLocal,
    IReadOnlyList<MatchView> Matches,
    string Version);

/// <summary>
/// Conferência dos fatos da rodada antes da apuração e publicação. <see cref="Publication"/>
/// resume a apuração vigente e é nula enquanto a rodada não foi publicada.
/// </summary>
public sealed record RoundReviewView(
    Guid RoundId,
    string RoundName,
    string Phase,
    int ScheduledMatches,
    int CompletedSheets,
    bool Ready,
    IReadOnlyList<RoundReviewMatchView> Matches,
    IReadOnlyList<RoundReviewPendingView> Pending,
    string Version,
    RoundPublicationView? Publication);

/// <summary>
/// A apuração vigente da rodada. Até <see cref="ConsolidatesAt"/> o resultado é provisório;
/// depois, consolidado. Os horários vêm também no fuso do campeonato, para a tela não
/// converter nada.
/// </summary>
public sealed record RoundPublicationView(
    int Revision,
    int ScoringRuleSetVersion,
    DateTimeOffset PublishedAt,
    string PublishedAtLocal,
    DateTimeOffset ConsolidatesAt,
    string ConsolidatesAtLocal,
    bool Consolidated,
    int Entries,
    decimal? HighestTotal,
    decimal? AverageTotal);

public sealed record RoundReviewMatchView(
    Guid MatchId,
    string HomeTeamName,
    string AwayTeamName,
    string KickoffLocal,
    string Status,
    bool RequiresSheet,
    bool HasSheet,
    int? HomeScore,
    int? AwayScore,
    int Participants,
    IReadOnlyList<RoundReviewEventView> Events);

public sealed record RoundReviewEventView(string Type, int Quantity);

public sealed record RoundReviewPendingView(string Code, string Message, Guid? MatchId);
