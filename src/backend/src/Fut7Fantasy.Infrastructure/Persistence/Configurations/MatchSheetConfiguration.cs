using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class MatchSheetConfiguration : IEntityTypeConfiguration<MatchSheet>
{
    public void Configure(EntityTypeBuilder<MatchSheet> builder)
    {
        builder.ToTable("MatchSheets", Fut7FantasyDbContext.CompetitionsSchema, table =>
        {
            table.HasCheckConstraint("CK_MatchSheets_HomeScore", "[HomeScore] BETWEEN 0 AND 99");
            table.HasCheckConstraint("CK_MatchSheets_AwayScore", "[AwayScore] BETWEEN 0 AND 99");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasOne<Competition>().WithMany().HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Match>().WithOne().HasForeignKey<MatchSheet>(item => item.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.CompetitionId, item.MatchId }).IsUnique();
    }
}

public sealed class AthleteAppearanceConfiguration : IEntityTypeConfiguration<AthleteAppearance>
{
    public void Configure(EntityTypeBuilder<AthleteAppearance> builder)
    {
        builder.ToTable("AthleteAppearances", Fut7FantasyDbContext.CompetitionsSchema, table =>
        {
            table.HasCheckConstraint("CK_AthleteAppearances_GoalsConceded", "[GoalsConceded] BETWEEN 0 AND 99");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Position).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.HasOne<MatchSheet>().WithMany().HasForeignKey(item => item.MatchSheetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Athlete>().WithMany().HasForeignKey(item => item.AthleteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(item => item.RealTeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.MatchSheetId, item.AthleteId }).IsUnique();
        builder.HasIndex(item => new { item.CompetitionId, item.MatchSheetId });
    }
}

public sealed class StatEventConfiguration : IEntityTypeConfiguration<StatEvent>
{
    public void Configure(EntityTypeBuilder<StatEvent> builder)
    {
        builder.ToTable("StatEvents", Fut7FantasyDbContext.CompetitionsSchema, table =>
        {
            table.HasCheckConstraint("CK_StatEvents_Quantity", "[Quantity] > 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.RedCardReason).HasConversion<string>().HasMaxLength(24);
        builder.HasOne<MatchSheet>().WithMany().HasForeignKey(item => item.MatchSheetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Athlete>().WithMany().HasForeignKey(item => item.AthleteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.MatchSheetId, item.AthleteId, item.Type }).IsUnique();
        builder.HasIndex(item => new { item.CompetitionId, item.MatchSheetId });
    }
}
