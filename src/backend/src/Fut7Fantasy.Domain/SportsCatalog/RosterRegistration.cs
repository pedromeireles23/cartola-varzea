namespace Fut7Fantasy.Domain.SportsCatalog;

public enum RosterRegistrationStatus
{
    Active = 1,
    Released = 2,
}

/// <summary>
/// Inscrição imutável quanto ao time: desligar preserva preço e histórico e não vira
/// transferência silenciosa para outro elenco.
/// </summary>
public sealed class RosterRegistration
{
    private RosterRegistration()
    {
    }

    private RosterRegistration(
        Guid id,
        Guid competitionId,
        Guid athleteId,
        Guid realTeamId,
        AthleteDefinition definition,
        DateTimeOffset registeredAt)
    {
        Id = id;
        CompetitionId = competitionId;
        AthleteId = athleteId;
        RealTeamId = realTeamId;
        RegisteredAt = registeredAt;
        Status = RosterRegistrationStatus.Active;
        ApplyPricing(definition);
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public Guid AthleteId { get; private set; }

    public Guid RealTeamId { get; private set; }

    public PriceTier PriceTier { get; private set; }

    public decimal? InitialPriceOverride { get; private set; }

    public RosterRegistrationStatus Status { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public bool IsActive => Status == RosterRegistrationStatus.Active;

    public static RosterRegistration Create(
        Guid id,
        Guid competitionId,
        Guid athleteId,
        Guid realTeamId,
        AthleteDefinition definition,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id == Guid.Empty || competitionId == Guid.Empty || athleteId == Guid.Empty || realTeamId == Guid.Empty)
        {
            throw new ArgumentException("Inscrição, campeonato, atleta e time precisam ser identificados.");
        }

        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        return new RosterRegistration(id, competitionId, athleteId, realTeamId, definition, registeredAt);
    }

    /// <summary>
    /// O preço trava junto com a posição, na primeira abertura de mercado em que o atleta
    /// esteve disponível: a partir daí alguém pode tê-lo comprado por esse valor.
    /// </summary>
    public void UpdatePricing(AthleteDefinition definition, DateTimeOffset? marketLockedSince)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IsActive)
        {
            throw new InvalidOperationException("A inscrição desligada não pode ser alterada.");
        }

        if (marketLockedSince is not null && ChangesPricing(definition))
        {
            throw new InvalidOperationException("O preço não muda depois que o atleta entra no mercado.");
        }

        if (definition.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(definition));
        }

        ApplyPricing(definition);
    }

    public bool ChangesPricing(AthleteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.PriceTier != PriceTier || definition.InitialPriceOverride != InitialPriceOverride;
    }

    public void EnsureTeam(Guid realTeamId)
    {
        if (realTeamId != RealTeamId)
        {
            throw new InvalidOperationException("Atleta não pode ser transferido entre times no campeonato.");
        }
    }

    public void Release(DateTimeOffset releasedAt)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("O atleta já foi desligado.");
        }

        Status = RosterRegistrationStatus.Released;
        ReleasedAt = releasedAt;
    }

    private void ApplyPricing(AthleteDefinition definition)
    {
        PriceTier = definition.PriceTier;
        InitialPriceOverride = definition.InitialPriceOverride;
    }
}
