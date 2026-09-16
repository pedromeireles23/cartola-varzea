using Fut7Fantasy.Domain.Organizations;
using Microsoft.AspNetCore.Authorization;

namespace Fut7Fantasy.Infrastructure.Authorization;

/// <summary>
/// Exige associação com a organização dona do campeonato presente na rota. A
/// organização nunca vem do cliente: sai do próprio campeonato no banco.
/// </summary>
public sealed class CompetitionRoleRequirement(params OrganizationRole[] allowedRoles)
    : IAuthorizationRequirement
{
    public IReadOnlySet<OrganizationRole> AllowedRoles { get; } = allowedRoles.ToHashSet();
}
