using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Competitions", Fut7FantasyDbContext.CompetitionsSchema, table =>
        {
            // O domínio já recusa esses valores; o banco repete a regra para que uma
            // gravação fora do agregado não produza um campeonato impossível.
            table.HasCheckConstraint(
                "CK_Competitions_ResultsSlaBusinessDays",
                $"[ResultsSlaBusinessDays] BETWEEN {CompetitionSettings.MinBusinessDays}"
                + $" AND {CompetitionSettings.MaxBusinessDays}");
            table.HasCheckConstraint(
                "CK_Competitions_CorrectionWindowBusinessDays",
                $"[CorrectionWindowBusinessDays] BETWEEN {CompetitionSettings.MinBusinessDays}"
                + $" AND {CompetitionSettings.MaxBusinessDays}");
            table.HasCheckConstraint(
                "CK_Competitions_MarketCloseLeadTimeMinutes",
                "[MarketCloseLeadTimeMinutes] BETWEEN 0"
                + $" AND {(int)CompetitionSettings.MaxMarketCloseLeadTime.TotalMinutes}");
        });
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Name)
            .HasMaxLength(CompetitionSettings.NameMaxLength).IsRequired();
        builder.Property(competition => competition.Season)
            .HasMaxLength(CompetitionSettings.SeasonMaxLength).IsRequired();
        builder.Property(competition => competition.Modality).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(competition => competition.TimeZoneId)
            .HasMaxLength(CompetitionSettings.TimeZoneIdMaxLength).IsRequired();

        // O tipo time do SQL Server não passa de 24 horas; minutos inteiros bastam.
        builder.Property(competition => competition.MarketCloseLeadTime)
            .HasColumnName("MarketCloseLeadTimeMinutes")
            .HasConversion(value => (int)value.TotalMinutes, minutes => TimeSpan.FromMinutes(minutes));
        builder.Property(competition => competition.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(competition => competition.RowVersion).IsRowVersion();
        builder.Ignore(competition => competition.ModalityProfile);
        builder.Ignore(competition => competition.CanChangeModality);
        builder.Ignore(competition => competition.IsPublished);

        builder.HasOne<Organization>().WithMany().HasForeignKey(competition => competition.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(competition => competition.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(competition => new { competition.OrganizationId, competition.UpdatedAt });
        builder.HasIndex(competition => competition.Status);
    }
}
