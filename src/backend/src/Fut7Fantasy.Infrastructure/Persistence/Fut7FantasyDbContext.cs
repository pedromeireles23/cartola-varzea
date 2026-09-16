using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>Contexto unico da aplicacao. Cada modulo continua dono das proprias gravacoes.</summary>
public sealed class Fut7FantasyDbContext(DbContextOptions<Fut7FantasyDbContext> options)
    : DbContext(options)
{
    /// <summary>Schema padrao das tabelas da aplicacao.</summary>
    public const string Schema = "app";

    /// <summary>Historico de inicializacoes.</summary>
    public DbSet<StartupRecord> StartupRecords => Set<StartupRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Fut7FantasyDbContext).Assembly);
    }
}
