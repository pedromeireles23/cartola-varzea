using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Fantasy;

/// <summary>Dados do ativo no instante em que o mercado fechou.</summary>
public sealed record SnapshotAsset(
    AssetKind Kind,
    Guid Id,
    string Name,
    Position? Position,
    Guid RealTeamId,
    string RealTeamName,
    decimal Price);

/// <summary>
/// Escalação imutável que vale para uma participação numa rodada. O retrato guarda os
/// rótulos e as regras necessárias para que mudanças futuras no catálogo não reescrevam
/// o passado.
/// </summary>
public sealed class LineupSnapshot
{
    private readonly List<LineupSnapshotSlot> _slots = [];

    private LineupSnapshot()
    {
    }

    public Guid Id { get; private set; }

    public Guid EntryId { get; private set; }

    public Guid RoundId { get; private set; }

    public Guid CaptainAthleteId { get; private set; }

    public int ModalityProfileVersion { get; private set; }

    public int MaxStartersPerTeam { get; private set; }

    public int MaxAthletesPerTeam { get; private set; }

    /// <summary>Instante em que a escalação passou a valer, independentemente da gravação tardia.</summary>
    public DateTimeOffset MarketClosedAt { get; private set; }

    /// <summary>Instante em que o retrato idempotente foi materializado no banco.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    public IReadOnlyList<LineupSnapshotSlot> Slots => _slots;

    public static LineupSnapshot Capture(
        Guid id,
        Guid roundId,
        FantasyEntry entry,
        SquadRules rules,
        IReadOnlyDictionary<(AssetKind Kind, Guid Id), SnapshotAsset> assets,
        DateTimeOffset marketClosedAt,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(assets);
        if (id == Guid.Empty || roundId == Guid.Empty)
        {
            throw new ArgumentException("Snapshot e rodada precisam ser identificados.");
        }

        if (entry.JoinedAt > marketClosedAt)
        {
            throw new InvalidOperationException("A participação começou depois do fechamento desta rodada.");
        }

        if (capturedAt < marketClosedAt)
        {
            throw new InvalidOperationException("O snapshot só pode ser capturado depois do fechamento.");
        }

        if (entry.Issues(rules).Count > 0 || entry.CaptainAthleteId is not { } captainId)
        {
            throw new InvalidOperationException("Somente uma escalação completa pode virar snapshot.");
        }

        var snapshot = new LineupSnapshot
        {
            Id = id,
            EntryId = entry.Id,
            RoundId = roundId,
            CaptainAthleteId = captainId,
            ModalityProfileVersion = rules.Profile.Version,
            MaxStartersPerTeam = rules.TeamLimit.MaxStarters,
            MaxAthletesPerTeam = rules.TeamLimit.MaxAthletes,
            MarketClosedAt = marketClosedAt,
            CapturedAt = capturedAt,
        };

        foreach (var slot in entry.Slots)
        {
            if (!assets.TryGetValue((slot.Kind, slot.AssetId), out var asset)
                || asset.Position != slot.Position
                || asset.RealTeamId != slot.RealTeamId)
            {
                throw new InvalidOperationException("O catálogo não corresponde ao elenco que será congelado.");
            }

            snapshot._slots.Add(new LineupSnapshotSlot(
                Guid.CreateVersion7(),
                snapshot.Id,
                asset,
                slot.Role,
                slot.PurchasePrice));
        }

        return snapshot;
    }
}

/// <summary>Ativo e papel congelados dentro de uma escalação de rodada.</summary>
public sealed class LineupSnapshotSlot
{
    private LineupSnapshotSlot()
    {
    }

    internal LineupSnapshotSlot(
        Guid id,
        Guid snapshotId,
        SnapshotAsset asset,
        SquadRole role,
        decimal purchasePrice)
    {
        Id = id;
        SnapshotId = snapshotId;
        Kind = asset.Kind;
        AssetId = asset.Id;
        AssetName = asset.Name;
        Position = asset.Position;
        RealTeamId = asset.RealTeamId;
        RealTeamName = asset.RealTeamName;
        Role = role;
        Price = asset.Price;
        PurchasePrice = purchasePrice;
    }

    public Guid Id { get; private set; }

    public Guid SnapshotId { get; private set; }

    public AssetKind Kind { get; private set; }

    public Guid AssetId { get; private set; }

    public string AssetName { get; private set; } = string.Empty;

    public Position? Position { get; private set; }

    public Guid RealTeamId { get; private set; }

    public string RealTeamName { get; private set; } = string.Empty;

    public SquadRole Role { get; private set; }

    /// <summary>Preço de mercado que valia no fechamento.</summary>
    public decimal Price { get; private set; }

    /// <summary>Preço efetivamente pago, preservado para auditoria do patrimônio.</summary>
    public decimal PurchasePrice { get; private set; }
}
