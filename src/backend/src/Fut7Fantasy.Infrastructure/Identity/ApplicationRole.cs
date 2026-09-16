using Microsoft.AspNetCore.Identity;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Papel global da plataforma. Papel dentro de organizacao e campeonato nao mora
/// aqui: ele depende do escopo e entra na Fase 4.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Administrador da plataforma.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Conta publica de demonstracao, somente leitura.</summary>
    public const string DemoViewer = "DemoViewer";
}
