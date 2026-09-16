namespace Fut7Fantasy.Domain.PlatformAdministration;

/// <summary>Registro imutável de uma decisão administrativa relevante.</summary>
public sealed class AdministrativeAuditEntry
{
    private AdministrativeAuditEntry()
    {
    }

    private AdministrativeAuditEntry(
        Guid id,
        Guid actorUserId,
        string action,
        Guid targetId,
        string reason,
        DateTimeOffset occurredAt)
    {
        Id = id;
        ActorUserId = actorUserId;
        Action = action;
        TargetId = targetId;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public Guid TargetId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public static AdministrativeAuditEntry Create(
        Guid actorUserId,
        string action,
        Guid targetId,
        string reason,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new AdministrativeAuditEntry(
            Guid.CreateVersion7(), actorUserId, action, targetId, reason.Trim(), occurredAt);
    }
}
