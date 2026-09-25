using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Demo;

/// <summary>
/// Senhas das contas demo. Nunca têm valor padrão nem entram no repositório (04 §15): no
/// local vêm do <c>.env</c> pelo script de reset, na nuvem virão do cofre. A política é a
/// mesma da aplicação — doze caracteres no mínimo.
/// </summary>
internal sealed class DemoOptions
{
    public const string SectionName = "Demo";

    [Required(ErrorMessage = "Informe Demo:ViewerPassword, a senha pública do visitante.")]
    [MinLength(12)]
    public string ViewerPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe Demo:OrganizerPassword, a senha privada de quem organiza.")]
    [MinLength(12)]
    public string OrganizerPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe Demo:AdminPassword, a senha privada da administração.")]
    [MinLength(12)]
    public string AdminPassword { get; init; } = string.Empty;
}
