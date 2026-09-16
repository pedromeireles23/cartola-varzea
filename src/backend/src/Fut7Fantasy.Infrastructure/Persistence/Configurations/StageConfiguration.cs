using Fut7Fantasy.Domain.Competitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class StageConfiguration : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Stages", Fut7FantasyDbContext.CompetitionsSchema, table =>
            table.HasCheckConstraint(
                "CK_Stages_Sequence",
                $"[Sequence] BETWEEN 1 AND {StageDefinition.MaxStagesPerCompetition}"));
        builder.HasKey(stage => stage.Id);
        builder.Property(stage => stage.Name).HasMaxLength(StageDefinition.NameMaxLength).IsRequired();
        builder.Property(stage => stage.Format).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(stage => stage.RowVersion).IsRowVersion();

        // A ordem dos critérios é o dado; uma lista curta de nomes separados por vírgula
        // lê melhor numa consulta manual do que JSON com números da enumeração.
        builder.Property<List<TiebreakCriterion>>("_tiebreakers")
            .HasColumnName("Tiebreakers")
            .HasMaxLength(200)
            .IsRequired()
            .HasConversion(
                criteria => string.Join(',', criteria),
                text => text.Length == 0
                    ? new List<TiebreakCriterion>()
                    : text.Split(',', StringSplitOptions.None).Select(Enum.Parse<TiebreakCriterion>).ToList(),
                new ValueComparer<List<TiebreakCriterion>>(
                    (left, right) => left!.SequenceEqual(right!),
                    criteria => criteria.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    criteria => criteria.ToList()));
        builder.Ignore(stage => stage.Tiebreakers);

        builder.Ignore(stage => stage.Groups);
        builder.HasMany<StageGroup>("_groups")
            .WithOne()
            .HasForeignKey("StageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation("_groups").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Competition>().WithMany().HasForeignKey(stage => stage.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sem índice único na ordem: reordenar troca posições linha a linha. A unicidade é
        // garantida pela trava do campeonato em toda operação que mexe na ordem.
        builder.HasIndex(stage => new { stage.CompetitionId, stage.Sequence });
    }
}

public sealed class StageGroupConfiguration : IEntityTypeConfiguration<StageGroup>
{
    public void Configure(EntityTypeBuilder<StageGroup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("StageGroups", Fut7FantasyDbContext.CompetitionsSchema);
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.Property(group => group.Name).HasMaxLength(StageDefinition.GroupNameMaxLength).IsRequired();
    }
}
