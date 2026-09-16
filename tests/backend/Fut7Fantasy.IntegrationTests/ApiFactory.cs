using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Host de teste sem banco. A connection string e um placebo: a validacao de opcoes
/// no startup exige um valor, mas <see cref="IStartupLog"/> e o relogio sao trocados
/// por duplos, entao nenhuma conexao e aberta.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Instante fixo usado por todos os testes que dependem de relogio.</summary>
    public static readonly DateTimeOffset FixedNow = new(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);

    private const string UnreachableConnectionString =
        "Server=127.0.0.1,14330;Database=Fut7FantasyTests;User Id=sa;Password=nao-usada;"
        + "TrustServerCertificate=True;Connect Timeout=1";

    /// <summary>Registro de inicializacoes em memoria.</summary>
    public FakeStartupLog StartupLog { get; } = new();

    /// <summary>Relogio controlado pelo teste.</summary>
    public FakeTimeProvider Clock { get; } = new(FixedNow);

    /// <summary>
    /// Configuracao minima para a aplicacao subir num teste: as opcoes sao validadas
    /// no startup, entao todas as obrigatorias precisam existir.
    /// </summary>
    public static void ApplyRequiredSettings(IWebHostBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.ConnectionString)}",
            connectionString);
        builder.UseSetting("Authentication:PublicOrigin", "https://testes.local");
        builder.UseSetting("Email:Host", "127.0.0.1");
        builder.UseSetting("Email:Port", "1025");
        builder.UseSetting("Email:UseStartTls", "false");
        builder.UseSetting("Email:FromAddress", "nao-responda@testes.local");
        builder.UseSetting("Email:FromName", "Testes");
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ApplyRequiredSettings(builder, UnreachableConnectionString);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStartupLog>();
            services.AddSingleton<IStartupLog>(StartupLog);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>Duplo de <see cref="IStartupLog"/> que guarda os registros em memoria.</summary>
public sealed class FakeStartupLog : IStartupLog
{
    private readonly List<DateTimeOffset> _startups = [];

    /// <inheritdoc />
    public Task RecordAsync(string version, string environmentName, CancellationToken cancellationToken)
    {
        lock (_startups)
        {
            _startups.Add(ApiFactory.FixedNow);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StartupLogSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        lock (_startups)
        {
            return Task.FromResult(new StartupLogSummary(
                _startups.Count,
                _startups.Count == 0 ? null : _startups.Max()));
        }
    }
}
