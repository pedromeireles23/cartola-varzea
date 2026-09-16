namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>
/// Time real inscrito em um campeonato. Arquivamento preserva referências históricas e
/// impede que exclusão administrativa apague dados usados por rodadas futuras.
/// </summary>
public sealed class RealTeam
{
    private RealTeam()
    {
    }

    private RealTeam(Guid id, Guid competitionId, RealTeamDefinition definition, DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsArchived => ArchivedAt is not null;

    public static RealTeam Create(
        Guid id,
        Guid competitionId,
        RealTeamDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty)
        {
            throw new ArgumentException("Time e campeonato precisam ser identificados.");
        }

        return new RealTeam(id, competitionId, definition, createdAt);
    }

    public void Update(RealTeamDefinition definition, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (IsArchived)
        {
            throw new InvalidOperationException("Um time arquivado não pode ser alterado.");
        }

        Apply(definition);
        UpdatedAt = updatedAt;
    }

    public void Archive(DateTimeOffset archivedAt)
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("O time já está arquivado.");
        }

        ArchivedAt = archivedAt;
        UpdatedAt = archivedAt;
    }

    private void Apply(RealTeamDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        Name = definition.Normalized().Name;
    }
}
