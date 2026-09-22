using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Leagues;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class PrivateLeagueConfiguration : IEntityTypeConfiguration<PrivateLeague>
{
    public void Configure(EntityTypeBuilder<PrivateLeague> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PrivateLeagues", Fut7FantasyDbContext.LeaguesSchema);
        builder.HasKey(league => league.Id);
        builder.Property(league => league.Id).ValueGeneratedNever();
        builder.Property(league => league.Name)
            .HasMaxLength(LeagueDefinition.NameMaxLength).IsRequired();
        builder.Property(league => league.InviteCode).HasMaxLength(LeagueInviteCode.Length);
        builder.Property(league => league.RowVersion).IsRowVersion();

        builder.HasOne<Competition>().WithMany().HasForeignKey(league => league.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(league => league.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // O código é o que a rota de entrada procura, e dois iguais deixariam a busca
        // ambígua. O filtro deixa de fora as ligas fechadas, que não têm código.
        builder.HasIndex(league => league.InviteCode).IsUnique().HasFilter("[InviteCode] IS NOT NULL");
        builder.HasIndex(league => league.CompetitionId);
    }
}

public sealed class LeagueMembershipConfiguration : IEntityTypeConfiguration<LeagueMembership>
{
    public void Configure(EntityTypeBuilder<LeagueMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("LeagueMemberships", Fut7FantasyDbContext.LeaguesSchema);
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).ValueGeneratedNever();

        builder.HasOne<PrivateLeague>().WithMany().HasForeignKey(membership => membership.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FantasyEntry>().WithMany().HasForeignKey(membership => membership.EntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Entrar duas vezes na mesma liga não é entrar de novo, mesmo sob corrida.
        builder.HasIndex(membership => new { membership.LeagueId, membership.EntryId }).IsUnique();
        builder.HasIndex(membership => membership.UserId);
    }
}
