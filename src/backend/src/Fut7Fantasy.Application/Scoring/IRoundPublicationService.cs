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

public sealed record RoundPublicationResult(RoundPublicationOutcome Outcome, IReadOnlyList<RoundError> Errors)
{
    public static RoundPublicationResult Of(RoundPublicationOutcome outcome) => new(outcome, []);

    public static RoundPublicationResult Invalid(params RoundError[] errors) =>
        new(RoundPublicationOutcome.Invalid, errors);
}
