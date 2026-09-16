namespace Fut7Fantasy.Domain.Organizations;

/// <summary>Convite de uso único para ingressar como auxiliar.</summary>
public sealed class OrganizationInvitation
{
    private OrganizationInvitation()
    {
    }

    private OrganizationInvitation(
        Guid id,
        Guid organizationId,
        Guid invitedByUserId,
        string invitedEmail,
        string invitedEmailNormalized,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        OrganizationId = organizationId;
        InvitedByUserId = invitedByUserId;
        InvitedEmail = invitedEmail;
        InvitedEmailNormalized = invitedEmailNormalized;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = OrganizationInvitationStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public string InvitedEmail { get; private set; } = string.Empty;

    public string InvitedEmailNormalized { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public OrganizationInvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static OrganizationInvitation Create(
        Guid id,
        Guid organizationId,
        Guid invitedByUserId,
        string invitedEmail,
        string invitedEmailNormalized,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(invitedEmailNormalized);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (id == Guid.Empty || organizationId == Guid.Empty || invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Convite, organização e responsável precisam ser identificados.");
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A expiração precisa ser futura.");
        }

        return new OrganizationInvitation(
            id,
            organizationId,
            invitedByUserId,
            invitedEmail.Trim(),
            invitedEmailNormalized,
            tokenHash,
            createdAt,
            expiresAt);
    }

    public void Accept(Guid userId, DateTimeOffset acceptedAt)
    {
        EnsurePending();
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A conta que aceitou precisa ser identificada.", nameof(userId));
        }

        Status = OrganizationInvitationStatus.Accepted;
        AcceptedByUserId = userId;
        AcceptedAt = acceptedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        EnsurePending();
        Status = OrganizationInvitationStatus.Revoked;
        RevokedAt = revokedAt;
    }

    private void EnsurePending()
    {
        if (Status != OrganizationInvitationStatus.Pending)
        {
            throw new InvalidOperationException("O convite não está pendente.");
        }
    }
}
