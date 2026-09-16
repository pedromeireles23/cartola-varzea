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
