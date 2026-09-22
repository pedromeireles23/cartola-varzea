using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Scoring;

/// <summary>
/// Publicação da rodada (01 §9, 03 §12): apura, grava a apuração e o extrato e muda os
/// preços, tudo numa transação. Só o proprietário publica; a policy da rota já garantiu.
/// </summary>
public interface IRoundPublicationService
{
    /// <summary>
    /// Publica a rodada em revisão. Publicar de novo uma rodada já publicada não grava
    /// nada e responde como sucesso: é o mesmo resultado, não uma segunda apuração.
    /// </summary>
    Task<RoundPublicationResult> PublishAsync(
        Guid competitionId,
        Guid roundId,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reabre uma rodada publicada para corrigir a súmula. Nada é recalculado agora: a
    /// apuração vigente continua a que está gravada, e a correção só vale na republicação.
    /// O motivo é obrigatório depois que a rodada consolidou (01 §9).
    /// </summary>
    Task<RoundPublicationResult> ReopenAsync(
        Guid competitionId,
        Guid roundId,
        string version,
        string? reason,
        CancellationToken cancellationToken);
}

public enum RoundPublicationOutcome
{
    Completed,
    NotFound,

    /// <summary>A rodada mudou desde a leitura; nada foi gravado.</summary>
    Conflict,

    /// <summary>Pendência que impede publicar: súmula, rodada anterior sem publicar.</summary>
    Invalid,

    /// <summary>A rodada não está em revisão.</summary>
    StatusLocked,
}

/// <summary>
/// O que a republicação de uma correção mudou, para a auditoria e para a confirmação da
/// tela. <see cref="Chained"/> conta as rodadas seguintes que ganharam revisão nova.
/// </summary>
public sealed record RoundCorrectionSummary(int Revision, int ChangedEntries, int Chained);

public sealed record RoundPublicationResult(
    RoundPublicationOutcome Outcome,
    IReadOnlyList<RoundError> Errors,
    RoundCorrectionSummary? Correction = null)
{
    public static RoundPublicationResult Of(RoundPublicationOutcome outcome) => new(outcome, []);

    public static RoundPublicationResult Invalid(params RoundError[] errors) =>
        new(RoundPublicationOutcome.Invalid, errors);
}
