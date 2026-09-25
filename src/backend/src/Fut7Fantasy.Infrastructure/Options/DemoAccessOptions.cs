using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>
/// Entrada da demonstração pública sem senha (Fase 12, decisão do Pedro em 2026-09-25):
/// o botão "Entrar como visitante" abre a sessão da conta <c>DemoViewer</c>. Desligada
/// por padrão; só o ambiente de demonstração a liga. A senha do visitante deixa de
/// precisar circular, e o que se ganha entrando é o mesmo que uma credencial pública
/// daria — só leitura, imposta no servidor (04 §15).
/// </summary>
public sealed class DemoAccessOptions
{
    public const string SectionName = "DemoAccess";

    /// <summary>Liga a entrada de visitante. Sem isto a rota responde como inexistente.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// A conta que o botão abre. Ela precisa ter o papel <c>DemoViewer</c>: apontar para
    /// qualquer outra conta não abre sessão nenhuma.
    /// </summary>
    [EmailAddress]
    [StringLength(254)]
    public string? ViewerEmail { get; set; }
}
