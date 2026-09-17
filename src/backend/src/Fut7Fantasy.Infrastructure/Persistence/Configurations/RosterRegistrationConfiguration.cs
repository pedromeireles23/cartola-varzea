using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class RosterRegistrationConfiguration : IEntityTypeConfiguration<RosterRegistration>
{
    public void Configure(EntityTypeBuilder<RosterRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RosterRegistrations", Fut7FantasyDbContext.SportsCatalogSchema, table =>
            table.HasCheckConstraint(
                "CK_RosterRegistrations_InitialPriceOverride",
                "[InitialPriceOverride] IS NULL OR [InitialPriceOverride] BETWEEN 1 AND 30"));
        builder.HasKey(registration => registration.Id);
        builder.Property(registration => registration.PriceTier)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(registration => registration.Status)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(registration => registration.InitialPriceOverride).HasPrecision(5, 2);
        builder.Ignore(registration => registration.IsActive);

        builder.HasOne<Competition>().WithMany().HasForeignKey(registration => registration.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Athlete>().WithOne().HasForeignKey<RosterRegistration>(registration => registration.AthleteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(registration => registration.RealTeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(registration => registration.AthleteId).IsUnique();
        builder.HasIndex(registration => new
        {
            registration.CompetitionId,
            registration.RealTeamId,
            registration.Status,
        });
    }
}
