namespace Fut7Fantasy.Domain.PlatformAdministration;

/// <summary>Pedido de uma conta para criar e administrar uma organização.</summary>
public sealed class OrganizerApplication
{
    private OrganizerApplication()
    {
    }

    private OrganizerApplication(
        Guid id,
        Guid applicantUserId,
        string organizationName,
        DateTimeOffset submittedAt)
    {
        Id = id;
        ApplicantUserId = applicantUserId;
        OrganizationName = organizationName;
        SubmittedAt = submittedAt;
        Status = OrganizerApplicationStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid ApplicantUserId { get; private set; }

    public string OrganizationName { get; private set; } = string.Empty;

    public OrganizerApplicationStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public string? DecisionReason { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static OrganizerApplication Submit(
        Guid id,
        Guid applicantUserId,
        string organizationName,
        DateTimeOffset submittedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        if (id == Guid.Empty || applicantUserId == Guid.Empty)
        {
            throw new ArgumentException("Solicitação e conta precisam ser identificadas.");
        }

        return new OrganizerApplication(id, applicantUserId, organizationName.Trim(), submittedAt);
    }

    public void Approve(
        Guid organizationId,
        Guid decidedByUserId,
        string reason,
        DateTimeOffset decidedAt)
    {
        EnsurePending();
        ValidateDecision(organizationId, decidedByUserId, reason);

        Status = OrganizerApplicationStatus.Approved;
        OrganizationId = organizationId;
        DecidedByUserId = decidedByUserId;
        DecisionReason = reason.Trim();
        DecidedAt = decidedAt;
    }

    public void Reject(Guid decidedByUserId, string reason, DateTimeOffset decidedAt)
    {
        EnsurePending();
        ValidateDecisionActor(decidedByUserId, reason);

        Status = OrganizerApplicationStatus.Rejected;
        DecidedByUserId = decidedByUserId;
        DecisionReason = reason.Trim();
        DecidedAt = decidedAt;
    }

    private static void ValidateDecision(Guid organizationId, Guid decidedByUserId, string reason)
    {
        ValidateDecisionActor(decidedByUserId, reason);
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("A organização precisa ser identificada.");
        }
    }

    private static void ValidateDecisionActor(Guid decidedByUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (decidedByUserId == Guid.Empty)
        {
            throw new ArgumentException("A decisão precisa identificar seu responsável.");
        }
    }

    private void EnsurePending()
    {
        if (Status != OrganizerApplicationStatus.Pending)
        {
            throw new InvalidOperationException("A solicitação já foi decidida.");
        }
    }
}
