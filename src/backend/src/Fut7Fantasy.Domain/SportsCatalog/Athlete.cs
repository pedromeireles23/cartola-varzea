namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>Identidade esportiva de um atleta dentro do campeonato.</summary>
public sealed class Athlete
{
    private Athlete()
    {
    }

    private Athlete(Guid id, Guid competitionId, AthleteDefinition definition, DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public string SportingName { get; private set; } = string.Empty;

    public Position Position { get; private set; }

    /// <summary>Depois deste instante a posição não pode mais ser alterada.</summary>
    public DateTimeOffset? FirstMarketAvailableAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Athlete Create(
        Guid id,
        Guid competitionId,
        AthleteDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty)
        {
            throw new ArgumentException("Atleta e campeonato precisam ser identificados.");
        }

        return new Athlete(id, competitionId, definition, createdAt);
    }

    public void Update(AthleteDefinition definition, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (FirstMarketAvailableAt is not null && definition.Position != Position)
        {
            throw new InvalidOperationException("A posição não muda depois que o atleta entra no mercado.");
        }

        Apply(definition);
        UpdatedAt = updatedAt;
    }

    public void MarkMarketAvailable(DateTimeOffset availableAt)
    {
        FirstMarketAvailableAt ??= availableAt;
        UpdatedAt = availableAt;
    }

    public void Touch(DateTimeOffset updatedAt) => UpdatedAt = updatedAt;

    private void Apply(AthleteDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        var normalized = definition.Normalized();
        SportingName = normalized.SportingName;
        Position = normalized.Position;
    }
}
