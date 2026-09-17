namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Grupo de uma fase de grupos. O Id é estável para receber os times na Fase 6.</summary>
public sealed class StageGroup
{
    private StageGroup()
    {
    }

    internal StageGroup(Guid id, string name, int sequence)
    {
        Id = id;
        Name = name;
        Sequence = sequence;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Sequence { get; private set; }

    internal void Rename(string name, int sequence)
    {
        Name = name;
        Sequence = sequence;
    }
}

/// <summary>
/// Fase do campeonato. Nome, formato e ordem são livres: o campeonato pode começar
/// diretamente em mata-mata, usar pontos corridos ou combinar grupos e repescagem.
/// </summary>
public sealed class Stage
{
    private readonly List<StageGroup> _groups = [];
    private List<TiebreakCriterion> _tiebreakers = [];

    private Stage()
    {
    }

    private Stage(Guid id, Guid competitionId, int sequence, DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        Sequence = sequence;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public StageFormat Format { get; private set; }

    /// <summary>Posição da fase no campeonato, começando em 1.</summary>
    public int Sequence { get; private set; }

    public IReadOnlyList<StageGroup> Groups => [.. _groups.OrderBy(group => group.Sequence)];

    /// <summary>Ordem dos critérios depois dos pontos; vazia no mata-mata.</summary>
    public IReadOnlyList<TiebreakCriterion> Tiebreakers => _tiebreakers;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Stage Create(
        Guid id,
        Guid competitionId,
        int sequence,
        StageDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty)
        {
            throw new ArgumentException("Fase e campeonato precisam ser identificados.");
        }

        if (definition.Groups.Any(group => group.Id is not null))
        {
            throw new ArgumentException("Uma fase nova só tem grupos novos.", nameof(definition));
        }

        var stage = new Stage(id, competitionId, sequence, createdAt);
        stage.MoveTo(sequence);
        stage.Apply(definition);
        return stage;
    }

    public void Update(StageDefinition definition, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!KnowsAllGroupsOf(definition))
        {
            throw new ArgumentException("Um dos grupos não pertence a esta fase.", nameof(definition));
        }

        Apply(definition);
        UpdatedAt = updatedAt;
    }

    /// <summary>Verdadeiro quando todo grupo com Id na definição já é desta fase.</summary>
    public bool KnowsAllGroupsOf(StageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Groups
            .Where(group => group.Id is not null)
            .All(group => _groups.Any(existing => existing.Id == group.Id));
    }

    public void MoveTo(int sequence)
    {
        if (sequence is < 1 or > StageDefinition.MaxStagesPerCompetition)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Posição de fase fora do limite.");
        }

        Sequence = sequence;
    }

    private void Apply(StageDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        var normalized = definition.Normalized();
        Name = normalized.Name;
        Format = normalized.Format;
        _tiebreakers = [.. normalized.Tiebreakers];

        // Grupos citados pelo Id mantêm a identidade; os que sumiram da lista saem.
        var kept = normalized.Groups.Where(group => group.Id is not null).Select(group => group.Id).ToHashSet();
        _groups.RemoveAll(group => !kept.Contains(group.Id));

        for (var index = 0; index < normalized.Groups.Count; index++)
        {
            var requested = normalized.Groups[index];
            if (requested.Id is { } groupId)
            {
                _groups.Single(group => group.Id == groupId).Rename(requested.Name, index + 1);
            }
            else
            {
                _groups.Add(new StageGroup(Guid.CreateVersion7(), requested.Name, index + 1));
            }
        }
    }
}
