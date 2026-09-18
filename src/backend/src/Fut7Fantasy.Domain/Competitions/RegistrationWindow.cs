namespace Fut7Fantasy.Domain.Competitions;

/// <summary>De onde vem o prazo de inscrição que vale agora.</summary>
public enum RegistrationDeadlineSource
{
    /// <summary>Data escolhida pelo organizador nas configurações.</summary>
    Configured = 1,

    /// <summary>Padrão: fechamento do mercado da última rodada da primeira fase.</summary>
    FirstStageLastRound = 2,

    /// <summary>Ainda não há rodada com jogo da primeira fase; as inscrições seguem abertas.</summary>
    NotYetDefined = 3,
}

/// <summary>
/// Prazo de inscrição de atletas (01 §7, decidido em 2026-09-14).
///
/// Sem data configurada, o prazo é o fechamento do mercado da última rodada que tem jogo
/// da primeira fase. O padrão acompanha o calendário: enquanto aquela rodada não abre o
/// mercado, o fechamento dela ainda não existe e as inscrições continuam abertas; uma
/// rodada nova da primeira fase empurra o prazo para depois.
/// </summary>
/// <param name="ClosesAt">Instante em que as inscrições fecham; nulo enquanto não há data.</param>
/// <param name="Source">De onde o prazo veio.</param>
/// <param name="RoundName">Rodada que define o prazo padrão, para a tela explicar.</param>
public sealed record RegistrationWindow(
    DateTimeOffset? ClosesAt,
    RegistrationDeadlineSource Source,
    string? RoundName)
{
    /// <summary>No instante do fechamento a inscrição já está encerrada, como o mercado.</summary>
    public bool IsOpenAt(DateTimeOffset now) => ClosesAt is null || now < ClosesAt;

    public static RegistrationWindow For(
        Competition competition,
        IEnumerable<Stage> stages,
        IEnumerable<Round> rounds,
        IEnumerable<Match> matches)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(matches);

        if (competition.RegistrationDeadline is { } configured)
        {
            return new(configured, RegistrationDeadlineSource.Configured, null);
        }

        if (stages.MinBy(stage => stage.Sequence) is not { } firstStage)
        {
            return new(null, RegistrationDeadlineSource.NotYetDefined, null);
        }

        var roundIds = matches
            .Where(match => match.StageId == firstStage.Id && match.Status != MatchStatus.Cancelled)
            .Select(match => match.RoundId)
            .ToHashSet();
        var lastRound = rounds
            .Where(round => roundIds.Contains(round.Id) && round.Status != RoundStatus.Cancelled)
            .MaxBy(round => round.Sequence);

        return lastRound is null
            ? new(null, RegistrationDeadlineSource.NotYetDefined, null)
            : new(lastRound.MarketCloseAt, RegistrationDeadlineSource.FirstStageLastRound, lastRound.Name);
    }
}
