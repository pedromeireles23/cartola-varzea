using System.Globalization;
using System.Security.Claims;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Authorization;

/// <summary>Resolve o escopo da rota no servidor e consulta a associação persistida.</summary>
public sealed class OrganizationRoleHandler(
    Fut7FantasyDbContext dbContext,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<OrganizationRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationRoleRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizationIdText = Convert.ToString(
            httpContext?.Request.RouteValues["organizationId"], CultureInfo.InvariantCulture);

        if (!Guid.TryParse(userIdText, out var userId)
            || !Guid.TryParse(organizationIdText, out var organizationId))
        {
            return;
        }

        var allowed = await dbContext.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(
                member => member.OrganizationId == organizationId
                    && member.UserId == userId
                    && requirement.AllowedRoles.Contains(member.Role),
                httpContext?.RequestAborted ?? CancellationToken.None)
            .ConfigureAwait(false);

        if (allowed)
        {
            context.Succeed(requirement);
        }
    }
}
