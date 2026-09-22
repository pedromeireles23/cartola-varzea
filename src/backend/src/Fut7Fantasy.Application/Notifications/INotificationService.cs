namespace Fut7Fantasy.Application.Notifications;

/// <summary>
/// A caixa de avisos da conta da sessão (01 §14). Lê e marca como lido sempre a conta
/// autenticada; não existe rota para ler a caixa de outra pessoa.
/// </summary>
public interface INotificationService
{
    /// <summary>Os avisos mais recentes primeiro, com quantos ainda não foram lidos.</summary>
    Task<NotificationInboxView> InboxAsync(CancellationToken cancellationToken);

    /// <summary>Marca todos os avisos não lidos da conta; devolve quantos mudaram.</summary>
    Task<int> MarkAllReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A caixa inteira. <see cref="Unread"/> conta tudo que está por ler, mesmo o que passou
/// do limite de <see cref="Items"/>: é o número que o sino mostra.
/// </summary>
public sealed record NotificationInboxView(int Unread, IReadOnlyList<NotificationView> Items);

/// <summary>
/// Um aviso pronto para a tela. <see cref="RoundId"/> não nulo significa que o aviso leva
/// à pontuação daquela rodada; a interface monta o endereço com o slug.
/// </summary>
public sealed record NotificationView(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string CompetitionSlug,
    Guid? RoundId,
    DateTimeOffset CreatedAt,
    bool Read);
