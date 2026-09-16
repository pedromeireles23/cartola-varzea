using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class RealTeamConfiguration : IEntityTypeConfiguration<RealTeam>
{
    public void Configure(EntityTypeBuilder<RealTeam> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RealTeams", Fut7FantasyDbContext.SportsCatalogSchema);
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Name).HasMaxLength(RealTeamDefinition.NameMaxLength).IsRequired();
        builder.Property(team => team.RowVersion).IsRowVersion();
        builder.Ignore(team => team.IsArchived);

        builder.HasOne<Competition>().WithMany().HasForeignKey(team => team.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(team => new { team.CompetitionId, team.Name }).IsUnique();
        builder.HasIndex(team => new { team.CompetitionId, team.ArchivedAt });
    }
}
