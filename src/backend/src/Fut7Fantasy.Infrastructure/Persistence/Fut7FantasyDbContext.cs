using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>
/// Contexto unico da aplicacao. Cada modulo continua dono das proprias gravacoes.
///
/// O Identity compartilha este contexto de proposito: aprovar um organizador vai
/// precisar criar organizacao e alterar papel na mesma transacao, e dois contextos
/// tornariam isso um problema distribuido sem necessidade.
/// </summary>
public sealed class Fut7FantasyDbContext(DbContextOptions<Fut7FantasyDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    /// <summary>Schema padrao das tabelas da aplicacao.</summary>
    public const string Schema = "app";

    /// <summary>Schema das tabelas de identidade.</summary>
    public const string IdentitySchema = "identity";

    /// <summary>Historico de inicializacoes.</summary>
    public DbSet<StartupRecord> StartupRecords => Set<StartupRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(Fut7FantasyDbContext).Assembly);
    }
}
