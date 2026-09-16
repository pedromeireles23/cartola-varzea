using System.Security.Claims;
using Fut7Fantasy.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>
/// Le o usuario da requisicao a partir do cookie de sessao validado pelo servidor.
/// Nenhum valor vem do corpo, da query ou de cabecalho enviado pelo cliente.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
