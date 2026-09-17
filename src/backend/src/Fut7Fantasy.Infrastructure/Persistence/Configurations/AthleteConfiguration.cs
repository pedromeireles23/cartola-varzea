using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

public sealed class AthleteConfiguration : IEntityTypeConfiguration<Athlete>
{
    public void Configure(EntityTypeBuilder<Athlete> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Athletes", Fut7FantasyDbContext.SportsCatalogSchema);
        builder.HasKey(athlete => athlete.Id);
        builder.Property(athlete => athlete.SportingName)
            .HasMaxLength(AthleteDefinition.SportingNameMaxLength).IsRequired();
        builder.Property(athlete => athlete.Position).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(athlete => athlete.RowVersion).IsRowVersion();

        builder.HasOne<Competition>().WithMany().HasForeignKey(athlete => athlete.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(athlete => new { athlete.CompetitionId, athlete.SportingName }).IsUnique();
    }
}
