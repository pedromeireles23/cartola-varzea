using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Leagues;
using Fut7Fantasy.Domain.Notifications;
using Fut7Fantasy.Domain.Organizations;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;
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

    public const string CompetitionsSchema = "competitions";

    public const string SportsCatalogSchema = "sports_catalog";

    public const string FantasySchema = "fantasy";

    public const string ScoringSchema = "scoring";

    public const string NotificationsSchema = "notifications";

    public const string LeaguesSchema = "leagues";

    /// <summary>Historico de inicializacoes.</summary>
    public DbSet<StartupRecord> StartupRecords => Set<StartupRecord>();

    public DbSet<OrganizerApplication> OrganizerApplications => Set<OrganizerApplication>();

    public DbSet<AdministrativeAuditEntry> AdministrativeAuditEntries => Set<AdministrativeAuditEntry>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    public DbSet<Competition> Competitions => Set<Competition>();

    public DbSet<Stage> Stages => Set<Stage>();

    public DbSet<StageParticipant> StageParticipants => Set<StageParticipant>();

    public DbSet<Round> Rounds => Set<Round>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<MatchSheet> MatchSheets => Set<MatchSheet>();

    public DbSet<AthleteAppearance> AthleteAppearances => Set<AthleteAppearance>();

    public DbSet<StatEvent> StatEvents => Set<StatEvent>();

    public DbSet<RealTeam> RealTeams => Set<RealTeam>();

    public DbSet<Athlete> Athletes => Set<Athlete>();

    public DbSet<RosterRegistration> RosterRegistrations => Set<RosterRegistration>();

    public DbSet<Coach> Coaches => Set<Coach>();

    public DbSet<FantasyEntry> FantasyEntries => Set<FantasyEntry>();

    public DbSet<SquadSlot> SquadSlots => Set<SquadSlot>();

    public DbSet<LineupSnapshot> LineupSnapshots => Set<LineupSnapshot>();

    public DbSet<LineupSnapshotSlot> LineupSnapshotSlots => Set<LineupSnapshotSlot>();

    public DbSet<RoundCalculation> RoundCalculations => Set<RoundCalculation>();

    public DbSet<AssetPriceChange> AssetPriceChanges => Set<AssetPriceChange>();

    public DbSet<EntryRoundResult> EntryRoundResults => Set<EntryRoundResult>();

    public DbSet<EntrySlotResult> EntrySlotResults => Set<EntrySlotResult>();

    public DbSet<AthleteScoreLine> AthleteScoreLines => Set<AthleteScoreLine>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<PrivateLeague> PrivateLeagues => Set<PrivateLeague>();

    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(Fut7FantasyDbContext).Assembly);
    }
}
