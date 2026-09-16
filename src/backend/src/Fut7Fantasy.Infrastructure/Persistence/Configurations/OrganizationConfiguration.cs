using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Organizations", Fut7FantasyDbContext.OrganizationsSchema);
        builder.HasKey(organization => organization.Id);
        builder.Property(organization => organization.Name).HasMaxLength(120).IsRequired();
        builder.Property(organization => organization.CreatedAt).IsRequired();
    }
}
public sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("OrganizationMembers", Fut7FantasyDbContext.OrganizationsSchema);
        builder.HasKey(member => new { member.OrganizationId, member.UserId });
        builder.Property(member => member.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(member => member.JoinedAt).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(member => member.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => member.UserId);
    }
}

public sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("OrganizationInvitations", Fut7FantasyDbContext.OrganizationsSchema);
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.InvitedEmail).HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.InvitedEmailNormalized).HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(invitation => invitation.RowVersion).IsRowVersion();
        builder.HasOne<Organization>().WithMany().HasForeignKey(invitation => invitation.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(invitation => invitation.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => new
        {
            invitation.OrganizationId,
            invitation.InvitedEmailNormalized,
            invitation.Status,
        });
    }
}
