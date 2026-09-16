using System.Security.Cryptography;
using System.Text;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Organizations;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Infrastructure.Organizations;

public sealed class OrganizationTeamService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    UserManager<ApplicationUser> users,
    IEmailSender emailSender,
    TimeProvider clock,
    IOptions<AuthenticationOptions> authenticationOptions,
    IOptions<OrganizationOptions> organizationOptions,
    ILogger<OrganizationTeamService> logger) : IOrganizationTeamService
{
    /// <summary>Tela do frontend que aceita o convite de auxiliar.</summary>
    public const string InvitationPath = "/organizar/convite";

    private readonly AuthenticationOptions _authentication = authenticationOptions.Value;
    private readonly OrganizationOptions _organizations = organizationOptions.Value;

    public async Task<OrganizationTeamView> GetAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await dbContext.Organizations
            .AsNoTracking()
            .SingleAsync(item => item.Id == organizationId, cancellationToken)
            .ConfigureAwait(false);

        var assistants = await (
            from member in dbContext.OrganizationMembers.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on member.UserId equals user.Id
            where member.OrganizationId == organizationId && member.Role == OrganizationRole.Assistant
            orderby user.DisplayName
            select new OrganizationMemberView(user.Id, user.DisplayName, user.Email!, member.JoinedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var invitations = await dbContext.OrganizationInvitations
            .AsNoTracking()
            .Where(invitation => invitation.OrganizationId == organizationId)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OrganizationTeamView(
            organization.Id,
            organization.Name,
            assistants,
            [.. invitations.Select(invitation => ToView(invitation, now))]);
    }

    public async Task<OrganizationInvitationView> InviteAssistantAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken)
    {
        var invitedBy = RequiredUserId();
        var organization = await dbContext.Organizations
            .SingleAsync(item => item.Id == organizationId, cancellationToken)
            .ConfigureAwait(false);
        var normalizedEmail = users.NormalizeEmail(email)
            ?? throw new InvalidOperationException("Não foi possível normalizar o e-mail.");
        var now = clock.GetUtcNow();

        var previous = await dbContext.OrganizationInvitations
            .Where(invitation => invitation.OrganizationId == organizationId
                && invitation.InvitedEmailNormalized == normalizedEmail
                && invitation.Status == OrganizationInvitationStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var previousInvitation in previous)
        {
            previousInvitation.Revoke(now);
            AddAudit(invitedBy, "OrganizationAssistantInvitationRevoked", previousInvitation.Id, now);
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var invitation = OrganizationInvitation.Create(
            Guid.CreateVersion7(),
            organizationId,
            invitedBy,
            email,
            normalizedEmail,
            HashToken(token),
            now,
            now.Add(_organizations.InvitationLifetime));
        dbContext.OrganizationInvitations.Add(invitation);
        AddAudit(invitedBy, "OrganizationAssistantInvited", invitation.Id, now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Rota da área de organização (02 §9.1). O token vai na query, nunca no
        // caminho, para a interface poder retirá-lo da URL assim que o ler.
        var link = new Uri(
            new Uri(_authentication.PublicOrigin),
            $"{InvitationPath}?token={Uri.EscapeDataString(token)}");
        var (subject, html, text) = OrganizationInvitationEmails.AssistantInvitation(
            organization.Name, link, invitation.ExpiresAt);

        try
        {
            await emailSender.SendAsync(email, subject, html, text, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Convite persistido pode ser reenviado; SMTP não desfaz a operação.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            OrganizationEvents.InvitationEmailFailed(logger, invitation.Id, exception);
        }

        return ToView(invitation, now);
    }

    public async Task<InvitationActionResult> AcceptInvitationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var userId = RequiredUserId();
        var invitation = await dbContext.OrganizationInvitations
            .SingleOrDefaultAsync(item => item.TokenHash == HashToken(token), cancellationToken)
            .ConfigureAwait(false);
        if (invitation is null)
        {
            return new InvitationActionResult(InvitationActionOutcome.NotFound, null);
        }

        var now = clock.GetUtcNow();
        if (invitation.Status == OrganizationInvitationStatus.Accepted)
        {
            var outcome = invitation.AcceptedByUserId == userId
                ? InvitationActionOutcome.AlreadyCompleted
                : InvitationActionOutcome.Conflict;
            return new InvitationActionResult(outcome, ToView(invitation, now));
        }

        if (invitation.Status != OrganizationInvitationStatus.Pending)
        {
            return new InvitationActionResult(InvitationActionOutcome.Conflict, ToView(invitation, now));
        }

        if (invitation.ExpiresAt <= now)
        {
            return new InvitationActionResult(InvitationActionOutcome.Expired, ToView(invitation, now));
        }

        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user?.NormalizedEmail is null
            || !string.Equals(
                user.NormalizedEmail,
                invitation.InvitedEmailNormalized,
                StringComparison.Ordinal))
        {
            return new InvitationActionResult(InvitationActionOutcome.EmailMismatch, ToView(invitation, now));
        }

        var alreadyMember = await dbContext.OrganizationMembers.AnyAsync(
            member => member.OrganizationId == invitation.OrganizationId && member.UserId == userId,
            cancellationToken).ConfigureAwait(false);
        if (alreadyMember)
        {
            return new InvitationActionResult(InvitationActionOutcome.Conflict, ToView(invitation, now));
        }

        invitation.Accept(userId, now);
        dbContext.OrganizationMembers.Add(OrganizationMember.CreateAssistant(
            invitation.OrganizationId, userId, now));
        AddAudit(userId, "OrganizationAssistantInvitationAccepted", invitation.Id, now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new InvitationActionResult(InvitationActionOutcome.Completed, ToView(invitation, now));
    }

    public async Task<MemberRemovalOutcome> RemoveAssistantAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId && item.UserId == userId,
            cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return MemberRemovalOutcome.NotFound;
        }

        // O proprietário não sai por aqui: a organização ficaria sem ninguém para
        // administrá-la. Transferência de propriedade é outra decisão.
        if (member.Role != OrganizationRole.Assistant)
        {
            return MemberRemovalOutcome.NotAnAssistant;
        }

        var now = clock.GetUtcNow();
        dbContext.OrganizationMembers.Remove(member);
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(),
            "OrganizationAssistantRemoved",
            userId,
            $"Auxiliar removido da organização {organizationId}.",
            now));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        OrganizationEvents.AssistantRemoved(logger, organizationId, userId);
        return MemberRemovalOutcome.Removed;
    }

    public async Task<InvitationActionResult> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.OrganizationInvitations.SingleOrDefaultAsync(
            item => item.Id == invitationId && item.OrganizationId == organizationId,
            cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return new InvitationActionResult(InvitationActionOutcome.NotFound, null);
        }

        var now = clock.GetUtcNow();
        if (invitation.Status == OrganizationInvitationStatus.Revoked)
        {
            return new InvitationActionResult(InvitationActionOutcome.AlreadyCompleted, ToView(invitation, now));
        }

        if (invitation.Status != OrganizationInvitationStatus.Pending)
        {
            return new InvitationActionResult(InvitationActionOutcome.Conflict, ToView(invitation, now));
        }

        invitation.Revoke(now);
        AddAudit(RequiredUserId(), "OrganizationAssistantInvitationRevoked", invitation.Id, now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new InvitationActionResult(InvitationActionOutcome.Completed, ToView(invitation, now));
    }

    private static string HashToken(string token) => WebEncoders.Base64UrlEncode(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");

    private void AddAudit(Guid actorUserId, string action, Guid invitationId, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            actorUserId,
            action,
            invitationId,
            "Ciclo de vida do convite de auxiliar.",
            occurredAt));

    private static OrganizationInvitationView ToView(
        OrganizationInvitation invitation,
        DateTimeOffset now)
    {
        var status = invitation.Status == OrganizationInvitationStatus.Pending && invitation.ExpiresAt <= now
            ? "Expired"
            : invitation.Status.ToString();
        return new OrganizationInvitationView(
            invitation.Id,
            invitation.OrganizationId,
            invitation.InvitedEmail,
            status,
            invitation.CreatedAt,
            invitation.ExpiresAt,
            invitation.AcceptedAt,
            invitation.RevokedAt);
    }
}
