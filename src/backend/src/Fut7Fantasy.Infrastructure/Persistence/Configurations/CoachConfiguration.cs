using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class CoachConfiguration : IEntityTypeConfiguration<Coach>
{
    public void Configure(EntityTypeBuilder<Coach> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Coaches", Fut7FantasyDbContext.SportsCatalogSchema, table =>
            table.HasCheckConstraint(
                "CK_Coaches_InitialPriceOverride",
                "[InitialPriceOverride] IS NULL OR [InitialPriceOverride] BETWEEN 1 AND 30"));
        builder.HasKey(coach => coach.Id);
        builder.Property(coach => coach.DisplayName).HasMaxLength(CoachDefinition.DisplayNameMaxLength);
        builder.Property(coach => coach.PriceTier).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(coach => coach.InitialPriceOverride).HasPrecision(5, 2);
        builder.Property(coach => coach.RowVersion).IsRowVersion();

        builder.HasOne<Competition>().WithMany().HasForeignKey(coach => coach.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RealTeam>().WithOne().HasForeignKey<Coach>(coach => coach.RealTeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(coach => coach.RealTeamId).IsUnique();
        builder.HasIndex(coach => new { coach.CompetitionId, coach.RealTeamId });
    }
}
