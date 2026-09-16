using Fut7Fantasy.Domain.Organizations;
using Microsoft.AspNetCore.Authorization;

namespace Fut7Fantasy.Infrastructure.Authorization;

/// <summary>Exige associação contextual com a organização presente na rota.</summary>
public sealed class OrganizationRoleRequirement(params OrganizationRole[] allowedRoles)
    : IAuthorizationRequirement
{
    public IReadOnlySet<OrganizationRole> AllowedRoles { get; } = allowedRoles.ToHashSet();
}
