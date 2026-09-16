using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Api.Security;

/// <summary>
/// Limite geral por IP para toda a API (04-seguranca §13).
///
/// Os padrões são os de produção. Só o ambiente local afrouxa o valor: a suíte E2E
/// sai toda de 127.0.0.1 e, em execuções seguidas, passava de 300 requisições por
/// minuto, o que derrubava telas inteiras com 429.
/// </summary>
public sealed class GlobalRateLimitOptions
{
    public const string SectionName = "RateLimiting:Global";

    /// <summary>Requisições permitidas por IP dentro da janela.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 300;

    /// <summary>Duração da janela fixa.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
