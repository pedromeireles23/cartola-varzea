using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Domain.PlatformAdministration;
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

    public const string PlatformAdministrationSchema = "platform";

    public const string OrganizationsSchema = "organizations";

    /// <summary>Historico de inicializacoes.</summary>
    public DbSet<StartupRecord> StartupRecords => Set<StartupRecord>();

    public DbSet<OrganizerApplication> OrganizerApplications => Set<OrganizerApplication>();

    public DbSet<AdministrativeAuditEntry> AdministrativeAuditEntries => Set<AdministrativeAuditEntry>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(Fut7FantasyDbContext).Assembly);
    }
}
