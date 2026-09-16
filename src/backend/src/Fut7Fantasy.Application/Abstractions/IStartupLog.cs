namespace Fut7Fantasy.Application.Abstractions;

/// <summary>Resumo do historico de inicializacoes da aplicacao.</summary>
/// <param name="Count">Quantidade de inicializacoes registradas.</param>
/// <param name="LastStartedAt">Instante da ultima inicializacao, ou <c>null</c> quando nao houver registro.</param>
public sealed record StartupLogSummary(int Count, DateTimeOffset? LastStartedAt);

/// <summary>
/// Porta de persistencia do registro de inicializacoes. E a primeira escrita real do
/// sistema e serve de corte vertical: prova Application -> Infrastructure -> SQL Server.
/// </summary>
public interface IStartupLog
{
    /// <summary>Registra uma inicializacao da aplicacao.</summary>
    Task RecordAsync(string version, string environmentName, CancellationToken cancellationToken);

    /// <summary>Le o resumo das inicializacoes ja registradas.</summary>
    Task<StartupLogSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
