namespace Fut7Fantasy.Application.Abstractions;

/// <summary>
/// Usuario da requisicao atual. Existe para que casos de uso nao dependam de
/// HttpContext nem de ClaimsPrincipal.
///
/// Autenticado nao significa autorizado: escopo de organizacao e campeonato entra
/// na Fase 4 e e sempre resolvido no servidor, nunca a partir de um ID enviado
/// pelo cliente (04-seguranca §4).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Identificador da conta, ou <c>null</c> quando anonimo.</summary>
    Guid? Id { get; }

    /// <summary>Verdadeiro quando ha sessao valida.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Verdadeiro quando a conta tem o papel global informado.</summary>
    bool IsInRole(string role);
}
