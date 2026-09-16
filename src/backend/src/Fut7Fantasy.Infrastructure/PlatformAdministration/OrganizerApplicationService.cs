using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.PlatformAdministration;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.PlatformAdministration;

public sealed class OrganizerApplicationService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    TimeProvider clock) : IOrganizerApplicationService
{
    public async Task<SubmissionResult> SubmitAsync(
        string organizationName,
        CancellationToken cancellationToken)
    {
        var userId = RequiredUserId();
        var existing = await dbContext.OrganizerApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                application => application.ApplicantUserId == userId
                    && application.Status == OrganizerApplicationStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new SubmissionResult(ToView(existing), Created: false);
        }

        var application = OrganizerApplication.Submit(
            Guid.CreateVersion7(), userId, organizationName, clock.GetUtcNow());
        dbContext.OrganizerApplications.Add(application);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SubmissionResult(ToView(application), Created: true);
    }

    public async Task<IReadOnlyList<OrganizerApplicationView>> GetMineAsync(
        CancellationToken cancellationToken)
    {
        var userId = RequiredUserId();
        var applications = await dbContext.OrganizerApplications
            .AsNoTracking()
            .Where(application => application.ApplicantUserId == userId)
            .OrderByDescending(application => application.SubmittedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. applications.Select(ToView)];
    }

    public async Task<IReadOnlyList<PendingOrganizerApplicationView>> GetPendingAsync(
        CancellationToken cancellationToken)
    {
        return await (
            from application in dbContext.OrganizerApplications.AsNoTracking()
            join applicant in dbContext.Users.AsNoTracking() on application.ApplicantUserId equals applicant.Id
            where application.Status == OrganizerApplicationStatus.Pending
            orderby application.SubmittedAt
            select new PendingOrganizerApplicationView(
                application.Id,
                application.OrganizationName,
                application.SubmittedAt,
                applicant.Id,
                applicant.DisplayName,
                applicant.Email!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ReviewResult> ApproveAsync(
        Guid applicationId,
        string reason,
        CancellationToken cancellationToken)
    {
        var reviewerId = RequiredUserId();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            // Uma repetição da execution strategy usa o mesmo DbContext. Limpar o
            // estado garante que uma tentativa revertida não pareça já aprovada.
            dbContext.ChangeTracker.Clear();

            // Fora da transação: só acontece na primeira aprovação da plataforma e
            // não pode segurar locks enquanto a aprovação roda.
            await EnsureOrganizerRoleExistsAsync().ConfigureAwait(false);

            // Read committed com UPDLOCK só na linha do pedido. Serializable colocava
            // range locks nas tabelas de papéis do Identity, e aprovações simultâneas
            // de pedidos diferentes entravam em deadlock (6 aprovações levavam ~16 s).
            // Duas aprovações do mesmo pedido continuam seriais: a segunda espera o
            // lock e já encontra o pedido aprovado.
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            var application = await dbContext.OrganizerApplications
                .FromSql($"""
                    SELECT * FROM [platform].[OrganizerApplications] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {applicationId}
                    """)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (application is null)
            {
                return new ReviewResult(ReviewOutcome.NotFound, null);
            }

            if (application.Status == OrganizerApplicationStatus.Approved)
            {
                return new ReviewResult(ReviewOutcome.AlreadyCompleted, ToView(application));
            }

            if (application.Status != OrganizerApplicationStatus.Pending)
            {
                return new ReviewResult(ReviewOutcome.ConflictingDecision, ToView(application));
            }

            var applicant = await users.FindByIdAsync(application.ApplicantUserId.ToString())
                .ConfigureAwait(false);
            if (applicant is null)
            {
                return new ReviewResult(ReviewOutcome.ConflictingDecision, ToView(application));
            }

            var now = clock.GetUtcNow();
            var organization = Organization.Create(Guid.CreateVersion7(), application.OrganizationName, now);
            var owner = OrganizationMember.CreateOwner(organization.Id, application.ApplicantUserId, now);

            application.Approve(organization.Id, reviewerId, reason, now);
            dbContext.Organizations.Add(organization);
            dbContext.OrganizationMembers.Add(owner);
            dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
                reviewerId, "OrganizerApplicationApproved", application.Id, reason, now));

            if (!await users.IsInRoleAsync(applicant, ApplicationRole.Organizer).ConfigureAwait(false))
            {
                EnsureSucceeded(await users.AddToRoleAsync(applicant, ApplicationRole.Organizer).ConfigureAwait(false));
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new ReviewResult(ReviewOutcome.Completed, ToView(application));
        });
    }

    public async Task<ReviewResult> RejectAsync(
        Guid applicationId,
        string reason,
        CancellationToken cancellationToken)
    {
        var reviewerId = RequiredUserId();
        var application = await dbContext.OrganizerApplications
            .SingleOrDefaultAsync(item => item.Id == applicationId, cancellationToken)
            .ConfigureAwait(false);

        if (application is null)
        {
            return new ReviewResult(ReviewOutcome.NotFound, null);
        }

        if (application.Status == OrganizerApplicationStatus.Rejected)
        {
            return new ReviewResult(ReviewOutcome.AlreadyCompleted, ToView(application));
        }

        if (application.Status != OrganizerApplicationStatus.Pending)
        {
            return new ReviewResult(ReviewOutcome.ConflictingDecision, ToView(application));
        }

        var now = clock.GetUtcNow();
        application.Reject(reviewerId, reason, now);
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            reviewerId, "OrganizerApplicationRejected", application.Id, reason, now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra decisão foi gravada entre a leitura e a gravação (o RowVersion mudou).
            // A resposta reflete o que ficou no banco, em vez de virar erro 500.
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.OrganizerApplications
                .AsNoTracking()
                .SingleAsync(item => item.Id == applicationId, cancellationToken)
                .ConfigureAwait(false);
            var outcome = current.Status == OrganizerApplicationStatus.Rejected
                ? ReviewOutcome.AlreadyCompleted
                : ReviewOutcome.ConflictingDecision;
            return new ReviewResult(outcome, ToView(current));
        }

        return new ReviewResult(ReviewOutcome.Completed, ToView(application));
    }

    private async Task EnsureOrganizerRoleExistsAsync()
    {
        if (await roles.RoleExistsAsync(ApplicationRole.Organizer).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var result = await roles.CreateAsync(new ApplicationRole
            {
                Id = Guid.CreateVersion7(),
                Name = ApplicationRole.Organizer,
            }).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return;
            }
        }
        catch (DbUpdateException)
        {
            // Outra aprovação criou o papel ao mesmo tempo; o índice único recusou este.
        }

        dbContext.ChangeTracker.Clear();
        if (!await roles.RoleExistsAsync(ApplicationRole.Organizer).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Não foi possível criar o papel Organizer.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Code)));
        }
    }

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

    private static OrganizerApplicationView ToView(OrganizerApplication application) => new(
        application.Id,
        application.ApplicantUserId,
        application.OrganizationName,
        application.Status.ToString(),
        application.SubmittedAt,
        application.DecidedAt,
        application.DecisionReason,
        application.OrganizationId);
}
