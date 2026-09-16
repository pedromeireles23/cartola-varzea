using Fut7Fantasy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.Startup;

/// <summary>
/// Registra uma linha a cada inicializacao da aplicacao.
/// Falha de banco nao derruba o processo: quem responde por disponibilidade e o
/// health check de readiness, e a aplicacao nao aplica migration sozinha ao subir.
/// </summary>
public sealed partial class StartupRecorder(
    IServiceScopeFactory scopeFactory,
    IApplicationEnvironment environment,
    ILogger<StartupRecorder> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var startupLog = scope.ServiceProvider.GetRequiredService<IStartupLog>();

        try
        {
            await startupLog.RecordAsync(environment.Version, environment.EnvironmentName, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Registrar a inicializacao e melhor esforco; nunca impede a aplicacao de subir.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            StartupNotRecorded(logger, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Nao foi possivel registrar a inicializacao no banco. A aplicacao continua subindo.")]
    private static partial void StartupNotRecorded(ILogger logger, Exception exception);
}
