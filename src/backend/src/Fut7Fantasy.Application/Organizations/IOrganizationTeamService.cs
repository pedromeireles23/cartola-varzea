namespace Fut7Fantasy.Application.Organizations;

/// <summary>Casos de uso da equipe auxiliar de uma organização.</summary>
public interface IOrganizationTeamService
{
    Task<OrganizationTeamView> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationInvitationView> InviteAssistantAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken);

    Task<InvitationActionResult> AcceptInvitationAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Retira um auxiliar da organização. O acesso acaba na requisição seguinte, porque
    /// as policies consultam a associação a cada requisição.
    /// </summary>
    Task<MemberRemovalOutcome> RemoveAssistantAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<InvitationActionResult> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken);
}

public sealed record OrganizationTeamView(
    Guid OrganizationId,
    string OrganizationName,
    IReadOnlyList<OrganizationMemberView> Assistants,
    IReadOnlyList<OrganizationInvitationView> Invitations);

public sealed record OrganizationMemberView(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTimeOffset JoinedAt);

public sealed record OrganizationInvitationView(
    Guid Id,
    Guid OrganizationId,
    string InvitedEmail,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt);

public enum MemberRemovalOutcome
{
    Removed,
    NotFound,
    NotAnAssistant,
}

public enum InvitationActionOutcome
{
    Completed,
    AlreadyCompleted,
    NotFound,
    Expired,
    EmailMismatch,
    Conflict,
}

public sealed record InvitationActionResult(
    InvitationActionOutcome Outcome,
    OrganizationInvitationView? Invitation);
