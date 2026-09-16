namespace Fut7Fantasy.Application.PlatformAdministration;

/// <summary>Casos de uso do fluxo de aprovação de organizadores.</summary>
public interface IOrganizerApplicationService
{
    Task<SubmissionResult> SubmitAsync(string organizationName, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerApplicationView>> GetMineAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingOrganizerApplicationView>> GetPendingAsync(CancellationToken cancellationToken);

    Task<ReviewResult> ApproveAsync(Guid applicationId, string reason, CancellationToken cancellationToken);

    Task<ReviewResult> RejectAsync(Guid applicationId, string reason, CancellationToken cancellationToken);
}

public sealed record OrganizerApplicationView(
    Guid Id,
    Guid ApplicantUserId,
    string OrganizationName,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    Guid? OrganizationId);

/// <summary>Item da fila de análise: inclui quem pediu, que só o Platform admin vê.</summary>
public sealed record PendingOrganizerApplicationView(
    Guid Id,
    string OrganizationName,
    DateTimeOffset SubmittedAt,
    Guid ApplicantUserId,
    string ApplicantDisplayName,
    string ApplicantEmail);

public sealed record SubmissionResult(OrganizerApplicationView Application, bool Created);

public enum ReviewOutcome
{
    Completed,
    AlreadyCompleted,
    NotFound,
    ConflictingDecision,
}

public sealed record ReviewResult(ReviewOutcome Outcome, OrganizerApplicationView? Application);
