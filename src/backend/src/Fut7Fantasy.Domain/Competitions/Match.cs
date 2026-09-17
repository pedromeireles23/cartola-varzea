namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Situação da partida dentro da rodada (01 §8).</summary>
public enum MatchStatus
{
    Scheduled = 1,

    /// <summary>Adiada: sai da apuração desta rodada e volta numa rodada futura.</summary>
    Postponed = 2,

    /// <summary>Cancelada: sai da apuração e não é remarcada.</summary>
    Cancelled = 3,
}

/// <summary>Dados que o organizador escolhe numa partida.</summary>
/// <param name="StageId">Fase a que o jogo pertence; define quem pode jogar.</param>
/// <param name="HomeTeamId">Mandante.</param>
/// <param name="AwayTeamId">Visitante.</param>
/// <param name="KickoffAt">Início, em UTC. A tela envia hora local e o servidor converte.</param>
public sealed record MatchDefinition(
    Guid StageId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTimeOffset KickoffAt)
{
    public IReadOnlyList<RoundError> Validate()
    {
        var errors = new List<RoundError>();
        if (StageId == Guid.Empty)
        {
            errors.Add(new(nameof(StageId), "Escolha a fase da partida."));
        }

        if (HomeTeamId == Guid.Empty)
        {
            errors.Add(new(nameof(HomeTeamId), "Escolha o time mandante."));
        }

        if (AwayTeamId == Guid.Empty)
        {
            errors.Add(new(nameof(AwayTeamId), "Escolha o time visitante."));
        }

        if (HomeTeamId != Guid.Empty && HomeTeamId == AwayTeamId)
        {
            errors.Add(new(nameof(AwayTeamId), "Um time não joga contra ele mesmo."));
        }

        return errors;
    }
}

/// <summary>
/// Uma partida da rodada. Pertence a uma fase, que é quem diz quais times podem jogar.
///
/// O mesmo time pode ter mais de uma partida na mesma rodada, o que é comum na várzea:
/// um domingo inteiro de jogos é uma rodada só (01 §7).
/// </summary>
public sealed class Match
{
    private Match()
    {
    }

    private Match(
        Guid id,
        Guid competitionId,
        Guid roundId,
        MatchDefinition definition,
        DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        RoundId = roundId;
        Status = MatchStatus.Scheduled;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    /// <summary>Repetido na linha para que toda consulta filtre pelo campeonato (03 §8).</summary>
    public Guid CompetitionId { get; private set; }

    public Guid RoundId { get; private set; }

    public Guid StageId { get; private set; }

    public Guid HomeTeamId { get; private set; }

    public Guid AwayTeamId { get; private set; }

    public DateTimeOffset KickoffAt { get; private set; }

    public MatchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Só partida marcada entra no cálculo do fechamento do mercado.</summary>
    public bool CountsForMarket => Status == MatchStatus.Scheduled;

    public static Match Create(
        Guid id,
        Guid competitionId,
        Guid roundId,
        MatchDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty || roundId == Guid.Empty)
        {
            throw new ArgumentException("Partida, campeonato e rodada precisam ser identificados.");
        }

        return new Match(id, competitionId, roundId, definition, createdAt);
    }

    /// <summary>Substitui fase, times e horário. Só faz sentido com a rodada em rascunho.</summary>
    public void Update(MatchDefinition definition, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Apply(definition);
        UpdatedAt = updatedAt;
    }

    /// <summary>Muda só o horário, o que continua valendo com o mercado aberto.</summary>
    public void Reschedule(DateTimeOffset kickoffAt, DateTimeOffset updatedAt)
    {
        EnsureNotClosed();
        KickoffAt = kickoffAt;
        Status = MatchStatus.Scheduled;
        UpdatedAt = updatedAt;
    }

    public void Postpone(DateTimeOffset updatedAt)
    {
        EnsureNotClosed();
        Status = MatchStatus.Postponed;
        UpdatedAt = updatedAt;
    }

    public void Cancel(DateTimeOffset updatedAt)
    {
        if (Status == MatchStatus.Cancelled)
        {
            throw new InvalidOperationException("A partida já está cancelada.");
        }

        Status = MatchStatus.Cancelled;
        UpdatedAt = updatedAt;
    }

    private void EnsureNotClosed()
    {
        if (Status == MatchStatus.Cancelled)
        {
            throw new InvalidOperationException("Uma partida cancelada não é remarcada.");
        }
    }

    private void Apply(MatchDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        StageId = definition.StageId;
        HomeTeamId = definition.HomeTeamId;
        AwayTeamId = definition.AwayTeamId;
        KickoffAt = definition.KickoffAt;
    }
}
