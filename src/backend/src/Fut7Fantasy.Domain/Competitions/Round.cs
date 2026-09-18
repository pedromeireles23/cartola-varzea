namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Um problema de validação da rodada, ligado ao campo que o causou.</summary>
public sealed record RoundError(string Field, string Message);

/// <summary>Dados que o organizador escolhe na rodada.</summary>
public sealed record RoundDefinition(string Name)
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 60;

    public RoundDefinition Normalized() => this with { Name = Name?.Trim() ?? string.Empty };

    public IReadOnlyList<RoundError> Validate() =>
        Normalized().Name.Length is < NameMinLength or > NameMaxLength
            ? [new(nameof(Name), $"Use de {NameMinLength} a {NameMaxLength} caracteres no nome da rodada.")]
            : [];
}

/// <summary>
/// Janela do fantasy: um conjunto de partidas com um mercado só (01 §7).
///
/// A rodada não pertence a uma fase. Um fim de semana pode ter jogos da fase de grupos e
/// de uma repescagem ao mesmo tempo, e o mercado é um só para o campeonato.
/// </summary>
public sealed class Round
{
    /// <summary>Acima disso a lista vira inútil; nenhum campeonato de várzea chega perto.</summary>
    public const int MaxRoundsPerCompetition = 60;

    private Round()
    {
    }

    private Round(Guid id, Guid competitionId, int sequence, RoundDefinition definition, DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        Sequence = sequence;
        Status = RoundStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Ordem da rodada no campeonato, começando em 1.</summary>
    public int Sequence { get; private set; }

    public RoundStatus Status { get; private set; }

    /// <summary>
    /// Instante em que o mercado fecha, congelado na abertura. Ele não se mexe depois:
    /// adiar uma partida não pode reabrir um mercado que já fechou (01 §8).
    /// </summary>
    public DateTimeOffset? MarketCloseAt { get; private set; }

    /// <summary>Início da primeira partida considerada na abertura do mercado.</summary>
    public DateTimeOffset? FirstKickoffAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Fim da janela de correção; a rodada vira consolidada sozinha (Fase 10).</summary>
    public DateTimeOffset? ConsolidatesAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Partidas só podem ser criadas, alteradas ou removidas no rascunho.</summary>
    public bool AcceptsMatchChanges => Status == RoundStatus.Draft;

    public static Round Create(
        Guid id,
        Guid competitionId,
        int sequence,
        RoundDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty)
        {
            throw new ArgumentException("Rodada e campeonato precisam ser identificados.");
        }

        var round = new Round(id, competitionId, sequence, definition, createdAt);
        round.MoveTo(sequence);
        return round;
    }

    public void Update(RoundDefinition definition, DateTimeOffset updatedAt)
    {
        Apply(definition);
        UpdatedAt = updatedAt;
    }

    public void MoveTo(int sequence)
    {
        if (sequence is < 1 or > MaxRoundsPerCompetition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence), sequence, "Posição de rodada fora do limite.");
        }

        Sequence = sequence;
    }

    /// <summary>
    /// Abre o mercado congelando o fechamento em <paramref name="firstKickoffAt"/> menos a
    /// antecedência do campeonato. Um fechamento que já passou é recusado: abrir um
    /// mercado nascido fechado só confundiria quem fosse escalar.
    /// </summary>
    public void OpenMarket(DateTimeOffset firstKickoffAt, TimeSpan marketCloseLeadTime, DateTimeOffset now)
    {
        EnsureTransition(RoundStatus.MarketOpen);

        var closeAt = firstKickoffAt - marketCloseLeadTime;
        if (closeAt <= now)
        {
            throw new InvalidOperationException("O mercado desta rodada já teria fechado.");
        }

        FirstKickoffAt = firstKickoffAt;
        MarketCloseAt = closeAt;
        Status = RoundStatus.MarketOpen;
        UpdatedAt = now;
    }

    /// <summary>
    /// Volta para rascunho para mexer nas partidas. Só vale enquanto o mercado não fechou:
    /// depois disso já existem escalações valendo aquela lista de jogos.
    /// </summary>
    public void ReopenForEditing(DateTimeOffset now)
    {
        EnsureTransition(RoundStatus.Draft);
        if (MarketIsClosed(now))
        {
            throw new InvalidOperationException("O mercado desta rodada já fechou.");
        }

        FirstKickoffAt = null;
        MarketCloseAt = null;
        Status = RoundStatus.Draft;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureTransition(RoundStatus.Cancelled);
        Status = RoundStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Encerra o lançamento normal e coloca os fatos esportivos em conferência. As
    /// pendências das súmulas são verificadas pelo caso de uso antes desta transição.
    /// </summary>
    public void BeginReview(DateTimeOffset now)
    {
        EnsureTransition(RoundStatus.UnderReview);
        if (PhaseAt(now) != RoundPhase.InProgress)
        {
            throw new InvalidOperationException("A rodada só entra em revisão depois do início das partidas.");
        }

        Status = RoundStatus.UnderReview;
        UpdatedAt = now;
    }

    /// <summary>Verdadeiro quando o relógio já passou do fechamento do mercado.</summary>
    public bool MarketIsClosed(DateTimeOffset now) =>
        Status == RoundStatus.MarketOpen && MarketCloseAt is { } closeAt && now >= closeAt;

    /// <summary>
    /// O ciclo completo, juntando o estado guardado com o que o relógio já alcançou.
    /// </summary>
    public RoundPhase PhaseAt(DateTimeOffset now) => Status switch
    {
        RoundStatus.Draft => RoundPhase.Draft,
        RoundStatus.Cancelled => RoundPhase.Cancelled,
        RoundStatus.UnderReview => RoundPhase.UnderReview,
        RoundStatus.Published => ConsolidatesAt is { } consolidatesAt && now >= consolidatesAt
            ? RoundPhase.Consolidated
            : RoundPhase.Published,
        RoundStatus.MarketOpen when FirstKickoffAt is { } kickoff && now >= kickoff => RoundPhase.InProgress,
        RoundStatus.MarketOpen when MarketIsClosed(now) => RoundPhase.MarketClosed,
        RoundStatus.MarketOpen => RoundPhase.MarketOpen,
        _ => throw new InvalidOperationException("Estado de rodada desconhecido."),
    };

    private void EnsureTransition(RoundStatus target)
    {
        if (!RoundLifecycle.Allows(Status, target))
        {
            throw new InvalidOperationException(
                $"Uma rodada em {Status} não passa para {target}.");
        }
    }

    private void Apply(RoundDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        Name = definition.Normalized().Name;
    }
}
