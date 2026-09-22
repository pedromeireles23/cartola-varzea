namespace Fut7Fantasy.Domain.Notifications;

/// <summary>
/// O que aconteceu. O texto que a pessoa lê é montado na leitura, a partir do tipo e dos
/// nomes atuais do campeonato e da rodada: assim renomear a rodada não deixa a caixa de
/// avisos falando de um nome que não existe mais.
/// </summary>
public enum NotificationKind
{
    /// <summary>A rodada foi apurada pela primeira vez.</summary>
    RoundPublished = 1,

    /// <summary>A rodada já apurada foi corrigida e republicada.</summary>
    RoundCorrected = 2,
}

/// <summary>
/// Aviso interno de uma conta (01 §14: convites e resultados não usam e-mail no MVP).
///
/// A notificação é um fato registrado, não uma mensagem enfileirada: ela nasce na mesma
/// transação do que a causou e some da caixa só quando a conta a lê. Falhar em criá-la
/// não pode invalidar a apuração (04 §17), e é por isso que a chave única existe: repetir
/// a operação não duplica o aviso.
/// </summary>
public sealed class Notification
{
    private Notification()
    {
    }

    private Notification(
        Guid id,
        Guid userId,
        NotificationKind kind,
        Guid competitionId,
        Guid? roundId,
        int? revision,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        CompetitionId = competitionId;
        RoundId = roundId;
        Revision = revision;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public Guid CompetitionId { get; private set; }

    /// <summary>Nulo nos avisos que não falam de uma rodada.</summary>
    public Guid? RoundId { get; private set; }

    /// <summary>Revisão da apuração que gerou o aviso; é ela que o torna único.</summary>
    public int? Revision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>
    /// O aviso de uma apuração. A primeira revisão anuncia o resultado; as seguintes
    /// anunciam a correção, porque só existem quando a rodada foi reaberta.
    /// </summary>
    public static Notification ForRound(
        Guid userId,
        Guid competitionId,
        Guid roundId,
        int revision,
        DateTimeOffset createdAt)
    {
        if (userId == Guid.Empty || competitionId == Guid.Empty || roundId == Guid.Empty)
        {
            throw new ArgumentException("Conta, campeonato e rodada precisam ser identificados.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        return new(
            Guid.CreateVersion7(),
            userId,
            revision == 1 ? NotificationKind.RoundPublished : NotificationKind.RoundCorrected,
            competitionId,
            roundId,
            revision,
            createdAt);
    }

    /// <summary>Marcar de novo não move a data: vale a primeira leitura.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
