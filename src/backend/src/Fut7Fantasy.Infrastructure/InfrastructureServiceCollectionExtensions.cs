using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Accounts;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Fantasy;
using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Application.Leagues;
using Fut7Fantasy.Application.Notifications;
using Fut7Fantasy.Application.Organizations;
using Fut7Fantasy.Application.PlatformAdministration;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Infrastructure.Authorization;
using Fut7Fantasy.Infrastructure.Competitions;
using Fut7Fantasy.Infrastructure.Email;
using Fut7Fantasy.Infrastructure.Fantasy;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Importing;
using Fut7Fantasy.Infrastructure.Leagues;
using Fut7Fantasy.Infrastructure.Notifications;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Organizations;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Scoring;
using Fut7Fantasy.Infrastructure.SportsCatalog;
using Fut7Fantasy.Infrastructure.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Infrastructure;

/// <summary>Registro da camada Infrastructure.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Nome do provedor de token da confirmacao de e-mail.</summary>
    private const string EmailConfirmationProvider = "EmailConfirmation";

    /// <summary>Adiciona persistencia, relogio e servicos de infraestrutura.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.SessionAbsoluteLifetime >= options.SessionLifetime,
                "O prazo absoluto da sessão não pode ser menor que o de inatividade.")
            .ValidateOnStart();

        services.AddOptions<OrganizationOptions>()
            .Bind(configuration.GetSection(OrganizationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PlatformAdministrationOptions>()
            .Bind(configuration.GetSection(PlatformAdministrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Relogio abstrato do proprio .NET: os testes injetam FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IApplicationEnvironment, HostApplicationEnvironment>();

        services.AddDbContext<Fut7FantasyDbContext>((provider, builder) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseSqlServer(database.ConnectionString, sql =>
            {
                sql.CommandTimeout(database.CommandTimeoutSeconds);
                sql.EnableRetryOnFailure();
                sql.MigrationsHistoryTable("__EFMigrationsHistory", Fut7FantasyDbContext.Schema);
            });
        });

        services.AddScoped<IStartupLog, EfStartupLog>();
        services.AddHostedService<StartupRecorder>();

        AddIdentity(services);
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IOrganizerApplicationService, OrganizerApplicationService>();
        services.AddScoped<InitialPlatformAdmin>();
        services.AddHostedService<InitialPlatformAdminGrant>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationTeamService, OrganizationTeamService>();
        services.AddScoped<IAuthorizationHandler, OrganizationRoleHandler>();
        services.AddScoped<ICompetitionManagementService, CompetitionManagementService>();
        services.AddScoped<ICompetitionStageService, CompetitionStageService>();
        services.AddScoped<ICompetitionPublicationService, CompetitionPublicationService>();
        services.AddScoped<IPublicCompetitionService, PublicCompetitionService>();
        services.AddScoped<IPublicFixtureService, PublicFixtureService>();
        services.AddScoped<IPublicCatalogService, PublicCatalogService>();
        services.AddScoped<ICompetitionRoundService, CompetitionRoundService>();
        services.AddScoped<IMatchSheetService, MatchSheetService>();
        services.AddScoped<IRealTeamService, RealTeamService>();
        services.AddScoped<IAthleteService, AthleteService>();
        services.AddScoped<ICoachService, CoachService>();
        services.AddScoped<ICatalogImportService, CatalogImportService>();
        services.AddScoped<IRoundStatisticsImportService, RoundStatisticsImportService>();
        services.AddScoped<LineupSnapshotMaterializer>();
        services.AddScoped<IFantasyService, FantasyService>();
        services.AddScoped<IRoundPublicationService, RoundPublicationService>();
        services.AddScoped<IFantasyScoreService, FantasyScoreService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRankingService, RankingService>();
        services.AddScoped<ILeagueService, LeagueService>();
        services.AddScoped<IAuthorizationHandler, CompetitionRoleHandler>();

        return services;
    }

    /// <summary>
    /// Adiciona o health check de prontidao. Fica separado do liveness: um banco fora
    /// do ar torna a aplicacao incapaz de atender, mas nao significa que o processo
    /// deva ser reiniciado.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddDbContextCheck<Fut7FantasyDbContext>(
            name: "database",
            failureStatus: HealthStatus.Unhealthy,
            tags: [HealthCheckTags.Readiness]);
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // Sessao em cookie, nunca token no browser (04-seguranca §5). A configuracao
        // do cookie em si fica no Api, que conhece o ambiente e o HTTPS.
        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Politica de senha por comprimento, sem regras de composicao
                // (04-seguranca §5): exigir simbolo e maiuscula empurra a pessoa
                // para senhas curtas e previsiveis, e gerenciadores de senha geram
                // segredos longos que passariam de qualquer forma.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.User.RequireUniqueEmail = true;

                // Operacoes sensiveis exigem e-mail verificado.
                options.SignIn.RequireConfirmedEmail = true;

                options.Lockout.AllowedForNewUsers = true;

                // Confirmacao de e-mail e recuperacao de senha usam provedores
                // distintos so para terem validades distintas (24h e 1h).
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationProvider;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<Fut7FantasyDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationProvider)
            .AddPasswordValidator<PasswordPolicy>();

        // Lockout e validade de token saem da configuracao, nao de constantes.
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<AuthenticationOptions>>((identity, auth) =>
            {
                identity.Lockout.MaxFailedAccessAttempts = auth.Value.MaxFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = auth.Value.LockoutDuration;
            });

        // Revalidar o security stamp a cada requisicao, em vez dos 30 minutos padrao.
        //
        // Sem isso, "redefinir a senha revoga as sessoes" (04-seguranca §5) so seria
        // verdade depois de meia hora: um cookie roubado continuaria valendo nesse
        // intervalo, justamente na janela em que a pessoa esta reagindo a um
        // comprometimento. O custo e uma consulta por chave primaria por requisicao
        // autenticada, aceitavel na escala deste projeto; se virar gargalo, o lugar
        // de rever e aqui, com medicao.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);

        // O token de recuperacao de senha vale 1 hora; o de confirmacao, 24.
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(1));
        services.AddOptions<EmailConfirmationTokenProviderOptions>()
            .Configure(options => options.TokenLifespan = TimeSpan.FromHours(24))
            .Validate(options => options.TokenLifespan > TimeSpan.Zero, "A validade precisa ser positiva.")
            .ValidateOnStart();
    }
}
