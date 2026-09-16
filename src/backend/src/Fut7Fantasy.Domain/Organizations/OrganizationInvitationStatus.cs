namespace Fut7Fantasy.Domain.Organizations;

/// <summary>Estado persistido de um convite para auxiliar uma organização.</summary>
public enum OrganizationInvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
}
