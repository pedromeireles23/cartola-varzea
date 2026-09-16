using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Api.Security;

/// <summary>
/// Limite dos endpoints de conta sensíveis a automação (04-seguranca §13).
///
/// Os padrões são os de produção. Só o ambiente local afrouxa o valor, porque a
/// suíte E2E faz todos os logins a partir do mesmo IP e esgotaria a cota em uma
/// execução; os testes de integração fixam o valor de produção.
/// </summary>
public sealed class AccountRateLimitOptions
{
    public const string SectionName = "RateLimiting:Account";

    /// <summary>Requisições permitidas por IP e caminho dentro da janela.</summary>
    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 10;

    /// <summary>Duração da janela fixa.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);
}
