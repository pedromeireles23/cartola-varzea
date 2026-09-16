using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>Parâmetros da administração da plataforma.</summary>
public sealed class PlatformAdministrationOptions
{
    public const string SectionName = "PlatformAdministration";

    /// <summary>
    /// E-mail da conta que recebe o papel <c>PlatformAdmin</c>. Ausente, nada é
    /// concedido; inválido, a aplicação recusa subir. O valor vem de user-secrets ou
    /// do Key Vault, nunca do <c>appsettings.json</c>: quem controla a configuração
    /// controla a administração.
    /// </summary>
    [EmailAddress]
    [StringLength(254)]
    public string? InitialAdminEmail { get; set; }
}
