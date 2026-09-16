using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fut7Fantasy.Infrastructure.Persistence.Configurations;

/// <summary>Mapeamento de <see cref="StartupRecord"/>.</summary>
public sealed class StartupRecordConfiguration : IEntityTypeConfiguration<StartupRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StartupRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StartupRecords");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Version).HasMaxLength(64).IsRequired();
        builder.Property(record => record.EnvironmentName).HasMaxLength(64).IsRequired();
        builder.Property(record => record.StartedAt).IsRequired();

        // A consulta do resumo ordena pelo instante mais recente.
        builder.HasIndex(record => record.StartedAt).IsDescending();
    }
}
