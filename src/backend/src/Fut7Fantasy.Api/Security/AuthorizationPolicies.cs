namespace Fut7Fantasy.Api.Security;

/// <summary>Nomes estáveis das policies de autorização da API.</summary>
public static class AuthorizationPolicies
{
    public const string PlatformAdmin = "platform-admin";

    public const string OrganizationMember = "organization-member";

    public const string OrganizationOwner = "organization-owner";

    public const string OrganizationOwnerWrite = "organization-owner-write";

    public const string AuthenticatedWrite = "authenticated-write";

    public const string CompetitionMember = "competition-member";

    public const string CompetitionStaffWrite = "competition-staff-write";

    public const string CompetitionOwnerWrite = "competition-owner-write";
}
