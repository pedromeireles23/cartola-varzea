namespace Fut7Fantasy.Application.PlatformAdministration;

/// <summary>Casos de uso do fluxo de aprovação de organizadores.</summary>
public interface IOrganizerApplicationService
{
    Task<SubmissionResult> SubmitAsync(string organizationName, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerApplicationView>> GetMineAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerApplicationView>> GetPendingAsync(CancellationToken cancellationToken);

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

public sealed record SubmissionResult(OrganizerApplicationView Application, bool Created);

public enum ReviewOutcome
{
    Completed,
    AlreadyCompleted,
    NotFound,
    ConflictingDecision,
}

public sealed record ReviewResult(ReviewOutcome Outcome, OrganizerApplicationView? Application);
