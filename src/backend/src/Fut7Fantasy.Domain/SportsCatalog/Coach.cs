namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>
/// Ativo de técnico do fantasy. Existe exatamente um por time e não precisa representar
/// uma pessoa identificada.
/// </summary>
public sealed class Coach
{
    private Coach()
    {
    }

    private Coach(
        Guid id,
        Guid competitionId,
        Guid realTeamId,
        CoachDefinition definition,
        DateTimeOffset createdAt)
    {
        Id = id;
        CompetitionId = competitionId;
        RealTeamId = realTeamId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Apply(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public Guid RealTeamId { get; private set; }

    public string? DisplayName { get; private set; }

    public PriceTier PriceTier { get; private set; }

    public decimal? InitialPriceOverride { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Coach Create(
        Guid id,
        Guid competitionId,
        Guid realTeamId,
        CoachDefinition definition,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty || realTeamId == Guid.Empty)
        {
            throw new ArgumentException("Técnico, campeonato e time precisam ser identificados.");
        }

        return new Coach(id, competitionId, realTeamId, definition, createdAt);
    }

    public void Update(CoachDefinition definition, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Apply(definition);
        UpdatedAt = updatedAt;
    }

    public string EffectiveName(string realTeamName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realTeamName);
        return DisplayName ?? $"Técnico do {realTeamName}";
    }

    private void Apply(CoachDefinition definition)
    {
        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        var normalized = definition.Normalized();
        DisplayName = normalized.DisplayName;
        PriceTier = normalized.PriceTier;
        InitialPriceOverride = normalized.InitialPriceOverride;
    }
}
