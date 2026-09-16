using Fut7Fantasy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>Implementacao de <see cref="IStartupLog"/> sobre EF Core.</summary>
public sealed class EfStartupLog(Fut7FantasyDbContext dbContext, TimeProvider timeProvider) : IStartupLog
{
    /// <inheritdoc />
    public async Task RecordAsync(string version, string environmentName, CancellationToken cancellationToken)
    {
        dbContext.StartupRecords.Add(new StartupRecord
        {
            Version = version,
            EnvironmentName = environmentName,
            StartedAt = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StartupLogSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var summary = await dbContext.StartupRecords
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Last = group.Max(record => (DateTimeOffset?)record.StartedAt) })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StartupLogSummary(summary?.Count ?? 0, summary?.Last);
    }
}
