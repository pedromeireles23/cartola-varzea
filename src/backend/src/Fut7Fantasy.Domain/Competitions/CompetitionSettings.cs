namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Um problema de validação ligado ao campo que o causou.</summary>
public sealed record CompetitionSettingsError(string Field, string Message);

/// <summary>
/// Dados configuráveis do campeonato. As regras ficam aqui, e não só na API, para que
/// a API e o agregado recusem exatamente os mesmos valores.
/// </summary>
public sealed record CompetitionSettings(
    string Name,
    string Season,
    Modality Modality,
    string TimeZoneId,
    TimeSpan MarketCloseLeadTime,
    int ResultsSlaBusinessDays,
    int CorrectionWindowBusinessDays)
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 120;
    public const int SeasonMaxLength = 40;
    public const int TimeZoneIdMaxLength = 64;

    /// <summary>Fuso padrão, porque o público inicial é brasileiro.</summary>
    public const string DefaultTimeZoneId = "America/Sao_Paulo";

    /// <summary>Prazo padrão para publicar o resultado depois da rodada (01 §9).</summary>
    public const int DefaultResultsSlaBusinessDays = 2;

    /// <summary>Janela padrão de correção antes da consolidação (01 §9).</summary>
    public const int DefaultCorrectionWindowBusinessDays = 3;

    public const int MinBusinessDays = 1;
    public const int MaxBusinessDays = 10;

    /// <summary>
    /// A antecedência do fechamento do mercado vai de zero (fecha no início da primeira
    /// partida) a três dias, o suficiente para rodadas de fim de semana.
    /// </summary>
    public static readonly TimeSpan MaxMarketCloseLeadTime = TimeSpan.FromHours(72);

    /// <summary>Valores normalizados: textos sem espaços nas pontas.</summary>
    public CompetitionSettings Normalized() => this with
    {
        Name = Name?.Trim() ?? string.Empty,
        Season = Season?.Trim() ?? string.Empty,
        TimeZoneId = TimeZoneId?.Trim() ?? string.Empty,
    };

    public IReadOnlyList<CompetitionSettingsError> Validate()
    {
        var normalized = Normalized();
        var errors = new List<CompetitionSettingsError>();

        if (normalized.Name.Length is < NameMinLength or > NameMaxLength)
        {
            errors.Add(new(
                nameof(Name), $"Use de {NameMinLength} a {NameMaxLength} caracteres no nome."));
        }

        if (normalized.Season.Length is 0 or > SeasonMaxLength)
        {
            errors.Add(new(nameof(Season), $"Informe a temporada com até {SeasonMaxLength} caracteres."));
        }

        if (!Enum.IsDefined(normalized.Modality))
        {
            errors.Add(new(nameof(Modality), "Escolha Fut7, futsal ou campo."));
        }

        if (!IsSupportedTimeZone(normalized.TimeZoneId))
        {
            errors.Add(new(nameof(TimeZoneId), "Escolha um fuso horário válido."));
        }

        if (normalized.MarketCloseLeadTime < TimeSpan.Zero
            || normalized.MarketCloseLeadTime > MaxMarketCloseLeadTime)
        {
            errors.Add(new(
                nameof(MarketCloseLeadTime),
                $"A antecedência vai de zero a {MaxMarketCloseLeadTime.TotalHours:0} horas."));
        }

        if (normalized.MarketCloseLeadTime.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            errors.Add(new(nameof(MarketCloseLeadTime), "Informe a antecedência em minutos inteiros."));
        }

        if (normalized.ResultsSlaBusinessDays is < MinBusinessDays or > MaxBusinessDays)
        {
            errors.Add(new(
                nameof(ResultsSlaBusinessDays),
                $"O prazo do resultado vai de {MinBusinessDays} a {MaxBusinessDays} dias úteis."));
        }

        if (normalized.CorrectionWindowBusinessDays is < MinBusinessDays or > MaxBusinessDays)
        {
            errors.Add(new(
                nameof(CorrectionWindowBusinessDays),
                $"A janela de correção vai de {MinBusinessDays} a {MaxBusinessDays} dias úteis."));
        }

        return errors;
    }

    /// <summary>
    /// Aceita só identificadores IANA (como <c>America/Sao_Paulo</c>), que são os mesmos
    /// no Windows, no Linux do Azure e no navegador. Um identificador do Windows, como
    /// <c>E. South America Standard Time</c>, seria resolvido só no servidor.
    /// </summary>
    public static bool IsSupportedTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && timeZoneId.Length <= TimeZoneIdMaxLength
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)
        && timeZone.HasIanaId;
}
