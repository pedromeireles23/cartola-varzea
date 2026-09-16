using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class OrganizerApplicationConfiguration : IEntityTypeConfiguration<OrganizerApplication>
{
    public void Configure(EntityTypeBuilder<OrganizerApplication> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("OrganizerApplications", Fut7FantasyDbContext.PlatformAdministrationSchema);
        builder.HasKey(application => application.Id);
        builder.Property(application => application.OrganizationName).HasMaxLength(120).IsRequired();
        builder.Property(application => application.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(application => application.DecisionReason).HasMaxLength(500);
        builder.Property(application => application.RowVersion).IsRowVersion();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(application => application.ApplicantUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(application => application.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(application => application.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(application => application.ApplicantUserId)
            .IsUnique()
            .HasFilter("[Status] = N'Pending'");
        builder.HasIndex(application => application.OrganizationId).IsUnique();
        builder.HasIndex(application => new { application.Status, application.SubmittedAt });
    }
}
public sealed class AdministrativeAuditEntryConfiguration : IEntityTypeConfiguration<AdministrativeAuditEntry>
{
    public void Configure(EntityTypeBuilder<AdministrativeAuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("AdministrativeAuditEntries", Fut7FantasyDbContext.PlatformAdministrationSchema);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Action).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(500).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.TargetId, entry.OccurredAt });
    }
}
