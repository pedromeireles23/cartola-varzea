using System.Globalization;

namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Por que um horário local não pôde virar um instante.</summary>
public enum LocalTimeFailure
{
    /// <summary>Texto que não é uma data e hora.</summary>
    NotATime,

    /// <summary>Fuso desconhecido no servidor.</summary>
    UnknownTimeZone,

    /// <summary>Horário que não existe naquele fuso, por causa do início do horário de verão.</summary>
    DoesNotExist,
}

/// <summary>
/// Conversão entre o horário que o organizador digita e o instante que o banco guarda.
///
/// A conversão é do servidor, e não do navegador, porque o fuso que vale é o do
/// campeonato: quem organiza de outro estado, ou viajando, marca o jogo no horário do
/// campeonato, não no do próprio relógio (03 §7).
/// </summary>
public static class CompetitionClock
{
    /// <summary>Formato que o `datetime-local` do navegador produz.</summary>
    private static readonly string[] LocalFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    /// <summary>
    /// Converte `2026-09-20T15:30` no fuso do campeonato para UTC. Horário ambíguo — o que
    /// acontece duas vezes quando o horário de verão termina — resolve pelo horário padrão,
    /// que é o segundo dos dois e o que a pessoa quis dizer na prática.
    /// </summary>
    public static bool TryToUtc(
        string? localDateTime,
        string timeZoneId,
        out DateTimeOffset utc,
        out LocalTimeFailure failure)
    {
        utc = default;
        failure = LocalTimeFailure.NotATime;

        if (!DateTime.TryParseExact(
            localDateTime?.Trim(),
            LocalFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return false;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            failure = LocalTimeFailure.UnknownTimeZone;
            return false;
        }

        if (timeZone.IsInvalidTime(parsed))
        {
            failure = LocalTimeFailure.DoesNotExist;
            return false;
        }

        var offset = timeZone.IsAmbiguousTime(parsed)
            ? timeZone.GetAmbiguousTimeOffsets(parsed).Min()
            : timeZone.GetUtcOffset(parsed);
        utc = new DateTimeOffset(parsed, offset).ToUniversalTime();
        return true;
    }

    /// <summary>
    /// O mesmo instante escrito no fuso do campeonato, para a tela mostrar sem precisar
    /// converter nada e sem depender do relógio de quem está olhando.
    /// </summary>
    public static string ToLocalText(DateTimeOffset instant, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
            ? found
            : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(instant, timeZone)
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }
}
