namespace Fut7Fantasy.Domain.Scoring;

/// <summary>
/// Até quando uma rodada publicada continua provisória (01 §9, "Apuração e correção"):
/// o fechamento do mercado mais a janela de correção, em dias úteis do fuso do
/// campeonato, e nunca antes de um dia útil depois da publicação — quem publica em cima
/// do prazo ainda deixa um dia para alguém reclamar.
///
/// Dia útil é segunda a sexta; feriados não entram na v1. A hora do dia é preservada no
/// fuso do campeonato: fechou num domingo às 10h, três dias úteis depois é quarta às 10h.
/// </summary>
public static class CorrectionWindow
{
    public static DateTimeOffset ConsolidatesAt(
        DateTimeOffset marketClosedAt,
        DateTimeOffset publishedAt,
        int correctionWindowBusinessDays,
        string timeZoneId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctionWindowBusinessDays);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var window = AddBusinessDays(marketClosedAt, correctionWindowBusinessDays, zone);
        var floor = AddBusinessDays(publishedAt, 1, zone);
        return window >= floor ? window : floor;
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset instant, int businessDays, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        var added = 0;
        while (added < businessDays)
        {
            local = local.AddDays(1);
            if (local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        // Um horário que não existe por causa do horário de verão anda para a frente.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
