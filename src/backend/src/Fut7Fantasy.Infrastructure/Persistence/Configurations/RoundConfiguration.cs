using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class RoundConfiguration : IEntityTypeConfiguration<Round>
{
    public void Configure(EntityTypeBuilder<Round> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Rounds", Fut7FantasyDbContext.CompetitionsSchema, table =>
        {
            // O domínio já recusa isso; o banco repete a regra porque uma rodada com
            // mercado aberto e sem fechamento seria um mercado que nunca fecha.
            table.HasCheckConstraint(
                "CK_Rounds_OpenMarketHasCloseAt",
                $"[Status] <> '{nameof(RoundStatus.MarketOpen)}' OR [MarketCloseAt] IS NOT NULL");
        });
        builder.HasKey(round => round.Id);
        builder.Property(round => round.Name)
            .HasMaxLength(RoundDefinition.NameMaxLength).IsRequired();
        builder.Property(round => round.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(round => round.RowVersion).IsRowVersion();
        builder.Property(round => round.CorrectionReason).HasMaxLength(Round.CorrectionReasonMaxLength);
        builder.Ignore(round => round.AcceptsMatchChanges);
        builder.Ignore(round => round.IsUnderCorrection);

        builder.HasOne<Competition>().WithMany().HasForeignKey(round => round.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(round => new { round.CompetitionId, round.Sequence });
    }
}

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Matches", Fut7FantasyDbContext.CompetitionsSchema);
        builder.HasKey(match => match.Id);
        builder.Property(match => match.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(match => match.RowVersion).IsRowVersion();
        builder.Ignore(match => match.CountsForMarket);

        builder.HasOne<Round>().WithMany().HasForeignKey(match => match.RoundId)
            .OnDelete(DeleteBehavior.Cascade);

        // `Restrict` nas demais: apagar fase ou time com partida precisa ser decisão
        // explícita de quem organiza, nunca cascata silenciosa.
        builder.HasOne<Competition>().WithMany().HasForeignKey(match => match.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Stage>().WithMany().HasForeignKey(match => match.StageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(match => match.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(match => match.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(match => new { match.RoundId, match.KickoffAt });
        builder.HasIndex(match => match.StageId);
        builder.HasIndex(match => new { match.CompetitionId, match.KickoffAt });
    }
}
