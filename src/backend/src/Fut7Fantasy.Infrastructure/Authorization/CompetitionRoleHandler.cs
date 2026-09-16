using System.Globalization;
using System.Security.Claims;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Authorization;

/// <summary>
/// Resolve o campeonato da rota e consulta a associação com a organização dona dele.
/// Campeonato inexistente e campeonato de outra organização dão o mesmo resultado, para
/// que trocar IDs não revele quais existem.
/// </summary>
public sealed class CompetitionRoleHandler(
    Fut7FantasyDbContext dbContext,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<CompetitionRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CompetitionRoleRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var competitionIdText = Convert.ToString(
            httpContext?.Request.RouteValues["competitionId"], CultureInfo.InvariantCulture);

        if (!Guid.TryParse(userIdText, out var userId)
            || !Guid.TryParse(competitionIdText, out var competitionId))
        {
            return;
        }

        var allowed = await (
                from competition in dbContext.Competitions.AsNoTracking()
                join member in dbContext.OrganizationMembers.AsNoTracking()
                    on competition.OrganizationId equals member.OrganizationId
                where competition.Id == competitionId
                    && member.UserId == userId
                    && requirement.AllowedRoles.Contains(member.Role)
                select member)
            .AnyAsync(httpContext?.RequestAborted ?? CancellationToken.None)
            .ConfigureAwait(false);

        if (allowed)
        {
            context.Succeed(requirement);
        }
    }
}
