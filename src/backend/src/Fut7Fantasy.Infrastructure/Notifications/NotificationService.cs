using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Notifications;
using Fut7Fantasy.Domain.Notifications;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Notifications;

/// <summary>
/// A caixa de avisos da conta da sessão. O texto é montado aqui, a partir do tipo e dos
/// nomes atuais: a notificação guarda o fato, não a frase.
/// </summary>
public sealed class NotificationService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : INotificationService
{
    /// <summary>Acima disso o sino vira uma lista de arquivo, que não é o que ele é.</summary>
    private const int MaxItems = 20;

    public async Task<NotificationInboxView> InboxAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from notification in dbContext.Notifications.AsNoTracking()
                join competition in dbContext.Competitions.AsNoTracking()
                    on notification.CompetitionId equals competition.Id
                where notification.UserId == UserId
                // O identificador é sequencial no tempo, então dois avisos gravados no mesmo
                // instante — a apuração grava tudo numa transação — têm ordem estável.
                orderby notification.CreatedAt descending, notification.Id descending
                select new
                {
                    notification.Id,
                    notification.Kind,
                    notification.RoundId,
                    notification.CreatedAt,
                    notification.ReadAt,
                    CompetitionName = competition.Name,
                    competition.Slug,
                })
            .Take(MaxItems)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roundIds = rows.Where(row => row.RoundId != null).Select(row => row.RoundId!.Value).ToArray();
        var roundNames = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => roundIds.Contains(round.Id))
            .ToDictionaryAsync(round => round.Id, round => round.Name, cancellationToken)
            .ConfigureAwait(false);

        // A contagem é de tudo, não só do que coube na lista: o sino não pode dizer 20
        // quando existem 30 por ler.
        var unread = await dbContext.Notifications
            .AsNoTracking()
            .CountAsync(notification => notification.UserId == UserId && notification.ReadAt == null, cancellationToken)
            .ConfigureAwait(false);

        return new(
            unread,
            [
                .. rows.Select(row =>
                {
                    var round = row.RoundId is { } id ? roundNames.GetValueOrDefault(id, "a rodada") : "a rodada";
                    return new NotificationView(
                        row.Id,
                        row.Kind.ToString(),
                        row.Kind == NotificationKind.RoundPublished
                            ? $"{round} apurada"
                            : $"{round} corrigida",
                        row.Kind == NotificationKind.RoundPublished
                            ? $"O resultado saiu em {row.CompetitionName}. Veja quanto você fez."
                            : $"{row.CompetitionName} republicou o resultado; sua pontuação pode ter mudado.",
                        row.Slug,
                        row.RoundId,
                        row.CreatedAt,
                        row.ReadAt is not null);
                }),
            ]);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var unread = await dbContext.Notifications
            .Where(notification => notification.UserId == UserId && notification.ReadAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (unread.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return unread.Count;
    }

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
