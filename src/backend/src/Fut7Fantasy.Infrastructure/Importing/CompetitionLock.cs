using System.Data;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Importing;

/// <summary>
/// Serializa as importações de um mesmo campeonato. A trava fica na linha do
/// campeonato, como no cadastro manual: read committed com UPDLOCK, sem range locks.
/// </summary>
internal static class CompetitionLock
{
    public static Task<T> RunAsync<T>(
        Fut7FantasyDbContext dbContext,
        Guid competitionId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            await dbContext.Competitions
                .FromSql($"""
                    SELECT * FROM [competitions].[Competitions] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {competitionId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await operation().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }
}
