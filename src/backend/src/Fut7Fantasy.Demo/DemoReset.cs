using Fut7Fantasy.Application;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Infrastructure;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Demo;

/// <summary>
/// O reset da demonstração: apaga o banco, recria pelas migrations e conta a história de
/// novo. Idempotente por construção — rodar duas vezes deixa o mesmo estado —, e travado
/// para nunca tocar um banco que não seja de demo (04 §15).
/// </summary>
internal static class DemoReset
{
    /// <summary>
    /// A mesma infraestrutura da API, com três trocas: o relógio, que o seed avança; a
    /// conta que age, que o seed escolhe; e o e-mail, que o seed nunca manda.
    /// </summary>
    public static IServiceCollection AddDemo(
        this IServiceCollection services,
        IConfiguration configuration,
        DateTimeOffset now)
    {
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Trinta dias antes de agora: a história começa três semanas antes do reset.
        var clock = new DemoClock(now.AddDays(-30));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(clock);

        var actor = new DemoActor();
        services.RemoveAll<ICurrentUser>();
        services.AddSingleton<ICurrentUser>(actor);
        services.AddSingleton(actor);

        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender, SilentEmailSender>();

        // Origem pública e remetente só servem para montar link e mandar e-mail, e o seed
        // não faz nenhum dos dois; sem eles a validação das opções pararia o reset.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.PublicOrigin))
            {
                options.PublicOrigin = "http://localhost";
            }
        });
        services.PostConfigure<EmailOptions>(options =>
        {
            options.Host = string.IsNullOrWhiteSpace(options.Host) ? "seed.invalid" : options.Host;
            options.FromAddress = string.IsNullOrWhiteSpace(options.FromAddress)
                ? "seed@demo.invalid"
                : options.FromAddress;
            options.FromName = string.IsNullOrWhiteSpace(options.FromName) ? "Cartola Várzea (seed)" : options.FromName;
        });

        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName))
            .ValidateDataAnnotations();
        services.AddSingleton<DemoSeeder>();
        return services;
    }

    public static async Task<DemoSummary> RunAsync(
        IServiceProvider services,
        string confirmedDatabase,
        CancellationToken cancellationToken)
    {
        // As senhas são conferidas antes de apagar qualquer coisa.
        _ = services.GetRequiredService<IOptions<DemoOptions>>().Value;

        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            var database = new SqlConnectionStringBuilder(dbContext.Database.GetConnectionString()).InitialCatalog;
            EnsureDemoDatabase(database, confirmedDatabase);

            await dbContext.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        var now = TimeProvider.System.GetUtcNow();
        return await services.GetRequiredService<DemoSeeder>().RunAsync(now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Só apaga o banco que a pessoa nomeou e que se declara de demo. Um banco de
    /// desenvolvimento, do E2E ou de produção futura nunca tem "Demo" no nome.
    /// </summary>
    public static void EnsureDemoDatabase(string actual, string confirmed)
    {
        if (string.IsNullOrWhiteSpace(actual)
            || !string.Equals(actual, confirmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"A connection string aponta para \"{actual}\", e o reset foi confirmado para "
                + $"\"{confirmed}\". Nada foi apagado.");
        }

        if (!actual.Contains("Demo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"O banco \"{actual}\" não é de demonstração: o reset só apaga bancos com \"Demo\" no nome. "
                + "Nada foi apagado.");
        }
    }
}
