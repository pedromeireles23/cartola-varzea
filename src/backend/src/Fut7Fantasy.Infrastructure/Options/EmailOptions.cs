using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>
/// Configuracao do e-mail transacional. Em desenvolvimento aponta para o Mailpit do
/// compose; em producao o segredo vem do Key Vault, nunca do repositorio.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Exigir STARTTLS. Falso apenas em desenvolvimento local.</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string FromName { get; set; } = string.Empty;
}
