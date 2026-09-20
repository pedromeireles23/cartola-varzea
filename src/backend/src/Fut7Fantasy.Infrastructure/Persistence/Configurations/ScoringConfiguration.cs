using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

/// <summary>
/// A apuração e o extrato de cada rodada, no schema `scoring`. Nada aqui é atualizado
/// depois de gravado: corrigir a rodada grava outra revisão (03 §7).
/// </summary>
public sealed class RoundCalculationConfiguration : IEntityTypeConfiguration<RoundCalculation>
{
    public void Configure(EntityTypeBuilder<RoundCalculation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RoundCalculations", Fut7FantasyDbContext.ScoringSchema, table =>
            table.HasCheckConstraint("CK_RoundCalculations_Revision", "[Revision] >= 1"));
        builder.HasKey(calculation => calculation.Id);
        builder.Property(calculation => calculation.Id).ValueGeneratedNever();
        builder.Property(calculation => calculation.Modality).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasOne<Competition>().WithMany().HasForeignKey(calculation => calculation.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Round>().WithMany().HasForeignKey(calculation => calculation.RoundId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(calculation => calculation.CalculatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Duas publicações simultâneas da mesma rodada não gravam a mesma revisão duas vezes.
        builder.HasIndex(calculation => new { calculation.RoundId, calculation.Revision }).IsUnique();
        builder.HasIndex(calculation => calculation.CompetitionId);

        Children(builder, calculation => calculation.Athletes, nameof(RoundCalculation.Athletes));
        Children(builder, calculation => calculation.AthleteLines, nameof(RoundCalculation.AthleteLines));
        Children(builder, calculation => calculation.Coaches, nameof(RoundCalculation.Coaches));
        Children(builder, calculation => calculation.Entries, nameof(RoundCalculation.Entries));
        Children(builder, calculation => calculation.EntrySlots, nameof(RoundCalculation.EntrySlots));
        Children(builder, calculation => calculation.Averages, nameof(RoundCalculation.Averages));
        Children(builder, calculation => calculation.Prices, nameof(RoundCalculation.Prices));
    }

    private static void Children<TChild>(
        EntityTypeBuilder<RoundCalculation> builder,
        System.Linq.Expressions.Expression<Func<RoundCalculation, IEnumerable<TChild>?>> navigation,
        string name)
        where TChild : class
    {
        builder.HasMany(navigation).WithOne().HasForeignKey(nameof(AthleteRoundResult.CalculationId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(name).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class AthleteRoundResultConfiguration : IEntityTypeConfiguration<AthleteRoundResult>
{
    public void Configure(EntityTypeBuilder<AthleteRoundResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("AthleteRoundResults", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).ValueGeneratedNever();
        builder.Property(result => result.Position).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(result => result.Points).HasPrecision(7, 2);
        builder.HasIndex(result => new { result.CalculationId, result.AthleteId }).IsUnique();
    }
}

public sealed class AthleteScoreLineConfiguration : IEntityTypeConfiguration<AthleteScoreLine>
{
    public void Configure(EntityTypeBuilder<AthleteScoreLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("AthleteScoreLines", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Item).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(line => line.Points).HasPrecision(7, 2);
        builder.HasIndex(line => new { line.CalculationId, line.AthleteId, line.MatchId, line.Item }).IsUnique();
    }
}

public sealed class CoachRoundResultConfiguration : IEntityTypeConfiguration<CoachRoundResult>
{
    public void Configure(EntityTypeBuilder<CoachRoundResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CoachRoundResults", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).ValueGeneratedNever();
        builder.Property(result => result.Points).HasPrecision(7, 2);
        builder.HasIndex(result => new { result.CalculationId, result.CoachId }).IsUnique();
    }
}

public sealed class EntryRoundResultConfiguration : IEntityTypeConfiguration<EntryRoundResult>
{
    public void Configure(EntityTypeBuilder<EntryRoundResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("EntryRoundResults", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).ValueGeneratedNever();
        builder.Property(result => result.Total).HasPrecision(8, 2);
        builder.Property(result => result.CaptainBonus).HasPrecision(7, 2);
        builder.HasOne<FantasyEntry>().WithMany().HasForeignKey(result => result.EntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(result => new { result.CalculationId, result.EntryId }).IsUnique();
        builder.HasIndex(result => result.EntryId);
    }
}

public sealed class EntrySlotResultConfiguration : IEntityTypeConfiguration<EntrySlotResult>
{
    public void Configure(EntityTypeBuilder<EntrySlotResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("EntrySlotResults", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).ValueGeneratedNever();
        builder.Property(result => result.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(result => result.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(result => result.Position).HasConversion<string>().HasMaxLength(16);
        builder.Property(result => result.Points).HasPrecision(7, 2);
        builder.HasIndex(result => new { result.CalculationId, result.EntryId, result.Kind, result.AssetId })
            .IsUnique();
    }
}

public sealed class PositionAverageConfiguration : IEntityTypeConfiguration<PositionAverage>
{
    public void Configure(EntityTypeBuilder<PositionAverage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PositionAverages", Fut7FantasyDbContext.ScoringSchema);
        builder.HasKey(average => average.Id);
        builder.Property(average => average.Id).ValueGeneratedNever();
        builder.Property(average => average.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(average => average.Position).HasConversion<string>().HasMaxLength(16);
        builder.Property(average => average.Average).HasPrecision(7, 2);
        builder.HasIndex(average => new { average.CalculationId, average.Kind, average.Position }).IsUnique();
    }
}

public sealed class AssetPriceChangeConfiguration : IEntityTypeConfiguration<AssetPriceChange>
{
    public void Configure(EntityTypeBuilder<AssetPriceChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Piso e teto também no banco: um preço fora da faixa quebraria o orçamento de todos.
        builder.ToTable("AssetPriceChanges", Fut7FantasyDbContext.ScoringSchema, table =>
            table.HasCheckConstraint("CK_AssetPriceChanges_NewPrice", "[NewPrice] >= 1 AND [NewPrice] <= 30"));
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(change => change.PreviousPrice).HasPrecision(5, 2);
        builder.Property(change => change.NewPrice).HasPrecision(5, 2);
        builder.Property(change => change.Variation).HasPrecision(5, 2);
        builder.Property(change => change.Average).HasPrecision(7, 2);
        builder.Property(change => change.Difference).HasPrecision(7, 2);
        builder.HasIndex(change => new { change.CalculationId, change.Kind, change.AssetId }).IsUnique();
        builder.HasIndex(change => new { change.Kind, change.AssetId });
    }
}
