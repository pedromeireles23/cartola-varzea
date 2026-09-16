using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.Startup;
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
    /// <summary>Adiciona persistencia, relogio e servicos de infraestrutura.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
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

        return services;
    }

    /// <summary>
    /// Adiciona o health check de prontidao. Fica separado do liveness: um banco fora do ar
    /// torna a aplicacao incapaz de atender, mas nao significa que o processo deva ser reiniciado.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddDbContextCheck<Fut7FantasyDbContext>(
            name: "database",
            failureStatus: HealthStatus.Unhealthy,
            tags: [HealthCheckTags.Readiness]);
    }
}
