using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Fantasy;

/// <summary>Tipo de ativo que o participante compra.</summary>
public enum AssetKind
{
    Athlete = 1,
    Coach = 2,
}

/// <summary>Papel da vaga no elenco. O técnico tem vaga própria, sem reserva.</summary>
public enum SquadRole
{
    Starter = 1,
    Bench = 2,
    Coach = 3,
}

/// <summary>
/// Uma vaga ocupada do elenco. O elenco é a escalação (decisão de 2026-09-18): comprar
/// ocupa uma vaga da posição, e vender a libera.
/// </summary>
public sealed class SquadSlot
{
    private SquadSlot()
    {
    }

    internal SquadSlot(
        Guid id,
        Guid entryId,
        MarketAsset asset,
        SquadRole role,
        DateTimeOffset acquiredAt)
    {
        Id = id;
        EntryId = entryId;
        Kind = asset.Kind;
        AssetId = asset.Id;
        RealTeamId = asset.RealTeamId;
        Position = asset.Position;
        Role = role;
        PurchasePrice = asset.Price;
        AcquiredAt = acquiredAt;
    }

    public Guid Id { get; private set; }

    public Guid EntryId { get; private set; }

    public AssetKind Kind { get; private set; }

    /// <summary>Atleta ou técnico do catálogo, conforme <see cref="Kind"/>.</summary>
    public Guid AssetId { get; private set; }

    /// <summary>Time do ativo, que não muda: é o que o limite por time real conta.</summary>
    public Guid RealTeamId { get; private set; }

    /// <summary>Posição do atleta; nulo para o técnico.</summary>
    public Position? Position { get; private set; }

    public SquadRole Role { get; private set; }

    /// <summary>Quanto saiu do saldo na compra, para o histórico; a venda usa o preço atual.</summary>
    public decimal PurchasePrice { get; private set; }

    public DateTimeOffset AcquiredAt { get; private set; }

    internal void MoveTo(SquadRole role) => Role = role;
}
