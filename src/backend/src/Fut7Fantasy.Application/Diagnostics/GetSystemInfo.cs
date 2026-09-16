using Fut7Fantasy.Application.Abstractions;

namespace Fut7Fantasy.Application.Diagnostics;

/// <summary>
/// Caso de uso do corte vertical: le o registro de inicializacoes e devolve a
/// informacao tecnica da aplicacao. O horario vem do <see cref="TimeProvider"/>,
/// nunca de <c>DateTime.UtcNow</c>, para que as regras temporais sejam testaveis.
/// </summary>
public sealed class GetSystemInfo(
    IApplicationEnvironment environment,
    IStartupLog startupLog,
    TimeProvider timeProvider)
{
    /// <summary>Executa a consulta.</summary>
    public async Task<SystemInfo> ExecuteAsync(CancellationToken cancellationToken)
    {
        var summary = await startupLog.GetSummaryAsync(cancellationToken).ConfigureAwait(false);

        return new SystemInfo(
            environment.Version,
            environment.EnvironmentName,
            timeProvider.GetUtcNow(),
            summary.Count,
            summary.LastStartedAt);
    }
}
