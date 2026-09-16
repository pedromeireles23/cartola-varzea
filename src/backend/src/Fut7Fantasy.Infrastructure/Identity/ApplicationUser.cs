using Microsoft.AspNetCore.Identity;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Conta de acesso. Guarda apenas o necessario para autenticar e identificar a
/// pessoa na interface; dado esportivo e de atleta vive em outro modulo.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Nome exibido na interface. Nunca e o e-mail.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Instante de criacao da conta, em UTC.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
