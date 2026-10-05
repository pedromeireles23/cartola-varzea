using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>
/// Configuracao do e-mail transacional. Em desenvolvimento aponta para o Mailpit do
/// compose; em producao o segredo vem do Key Vault, nunca do repositorio.
/// </summary>
public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";

    /// <summary>
    /// Desligado só numa publicação sem provedor de e-mail, como a demonstração na Azure
    /// (Fase 17, 04 §11): nada é enviado, e o cadastro e a recuperação de senha, que só
    /// funcionam por e-mail, deixam de existir. Ligado, o servidor SMTP é obrigatório.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Exigir STARTTLS. Falso apenas em desenvolvimento local.</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            yield return new ValidationResult("Email:Host é obrigatório com o e-mail ligado.", [nameof(Host)]);
        }

        if (string.IsNullOrWhiteSpace(FromAddress) || !new EmailAddressAttribute().IsValid(FromAddress))
        {
            yield return new ValidationResult(
                "Email:FromAddress precisa de um e-mail válido com o e-mail ligado.", [nameof(FromAddress)]);
        }

        if (string.IsNullOrWhiteSpace(FromName))
        {
            yield return new ValidationResult("Email:FromName é obrigatório com o e-mail ligado.", [nameof(FromName)]);
        }
    }
}
