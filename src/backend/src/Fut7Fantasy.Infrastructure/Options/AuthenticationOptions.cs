using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>Parametros de sessao e dos links enviados por e-mail.</summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// Origem publica usada para montar os links de verificacao e recuperacao.
    /// Nunca e derivada do cabecalho Host da requisicao, que o cliente controla.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string PublicOrigin { get; set; } = string.Empty;

    /// <summary>Validade da sessao sem uso; cada uso renova o prazo.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Validade maxima da sessao desde a entrada, que o uso nao renova. Sem ela,
    /// quem entra toda semana nunca mais digita a senha, e um cookie roubado vale
    /// enquanto for usado.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "90.00:00:00")]
    public TimeSpan SessionAbsoluteLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Tentativas de senha antes do bloqueio temporario.</summary>
    [Range(3, 20)]
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Duracao do bloqueio apos estourar as tentativas.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}
