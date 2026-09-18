using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class FantasyEntryConfiguration : IEntityTypeConfiguration<FantasyEntry>
{
    public void Configure(EntityTypeBuilder<FantasyEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Saldo negativo seria gasto além do orçamento; o banco recusa mesmo que a
        // aplicação erre (04 §8).
        builder.ToTable("Entries", Fut7FantasyDbContext.FantasySchema, table =>
            table.HasCheckConstraint("CK_Entries_Balance", "[Balance] >= 0"));
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Balance).HasPrecision(7, 2);
        builder.Property(entry => entry.RowVersion).IsRowVersion();

        builder.HasOne<Competition>().WithMany().HasForeignKey(entry => entry.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Uma participação por conta e campeonato, garantida pelo banco (01 §9).
        builder.HasIndex(entry => new { entry.CompetitionId, entry.UserId }).IsUnique();

        builder.HasMany(entry => entry.Slots).WithOne().HasForeignKey(slot => slot.EntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(entry => entry.Slots).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SquadSlotConfiguration : IEntityTypeConfiguration<SquadSlot>
{
    public void Configure(EntityTypeBuilder<SquadSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("SquadSlots", Fut7FantasyDbContext.FantasySchema);
        builder.HasKey(slot => slot.Id);

        // A vaga nasce dentro do agregado, com o id já definido. Sem isto, o EF vê um id
        // preenchido numa entidade nova da coleção e tenta um UPDATE em vez do INSERT.
        builder.Property(slot => slot.Id).ValueGeneratedNever();
        builder.Property(slot => slot.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(slot => slot.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(slot => slot.Position).HasConversion<string>().HasMaxLength(16);
        builder.Property(slot => slot.PurchasePrice).HasPrecision(5, 2);

        // O mesmo ativo nunca entra duas vezes no elenco, nem com duas compras simultâneas.
        builder.HasIndex(slot => new { slot.EntryId, slot.Kind, slot.AssetId }).IsUnique();
        builder.HasIndex(slot => new { slot.Kind, slot.AssetId });

        // O ativo pode ser atleta ou técnico, então não há chave estrangeira para ele; o
        // time, sim, é sempre um time real.
        builder.HasOne<RealTeam>().WithMany().HasForeignKey(slot => slot.RealTeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LineupSnapshotConfiguration : IEntityTypeConfiguration<LineupSnapshot>
{
    public void Configure(EntityTypeBuilder<LineupSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("LineupSnapshots", Fut7FantasyDbContext.FantasySchema);
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).ValueGeneratedNever();

        builder.HasOne<FantasyEntry>().WithMany().HasForeignKey(snapshot => snapshot.EntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Round>().WithMany().HasForeignKey(snapshot => snapshot.RoundId)
            .OnDelete(DeleteBehavior.Restrict);

        // MaterializaÃ§Ã£o preguiÃ§osa pode receber duas requisiÃ§Ãµes ao mesmo tempo; sÃ³ um
        // retrato por participaÃ§Ã£o e rodada vence no banco.
        builder.HasIndex(snapshot => new { snapshot.EntryId, snapshot.RoundId }).IsUnique();
        builder.HasIndex(snapshot => snapshot.RoundId);

        builder.HasMany(snapshot => snapshot.Slots).WithOne().HasForeignKey(slot => slot.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(snapshot => snapshot.Slots).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LineupSnapshotSlotConfiguration : IEntityTypeConfiguration<LineupSnapshotSlot>
{
    public void Configure(EntityTypeBuilder<LineupSnapshotSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("LineupSnapshotSlots", Fut7FantasyDbContext.FantasySchema);
        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id).ValueGeneratedNever();
        builder.Property(slot => slot.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(slot => slot.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(slot => slot.Position).HasConversion<string>().HasMaxLength(16);
        builder.Property(slot => slot.AssetName).HasMaxLength(120).IsRequired();
        builder.Property(slot => slot.RealTeamName).HasMaxLength(80).IsRequired();
        builder.Property(slot => slot.Price).HasPrecision(5, 2);
        builder.Property(slot => slot.PurchasePrice).HasPrecision(5, 2);
        builder.HasIndex(slot => new { slot.SnapshotId, slot.Kind, slot.AssetId }).IsUnique();
    }
}
