using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>Ativo do catálogo na apuração, com o preço que valia antes desta rodada.</summary>
public sealed record PricedAsset(AssetKind Kind, Guid AssetId, Position? Position, Guid RealTeamId, decimal Price);

/// <summary>A escalação congelada de uma participação na rodada.</summary>
public sealed record EntryLineup(Guid EntryId, FrozenLineup Lineup);

/// <summary>
/// A apuração de uma rodada (01 §9 e 03 §12): pontos de cada atleta, partida a partida e
/// item a item; do técnico; de cada participação, vaga a vaga; a média de cada posição e
/// o preço novo de cada ativo. Uma revisão nunca é alterada: corrigir a rodada gera outra
/// revisão, e a anterior continua gravada com a regra que usou.
///
/// Tudo sai de <see cref="Compute"/>, uma função das súmulas, dos retratos e dos preços:
/// as mesmas entradas dão sempre o mesmo resultado, em qualquer ordem.
/// </summary>
public sealed class RoundCalculation
{
    private readonly List<AthleteRoundResult> _athletes = [];
    private readonly List<AthleteScoreLine> _athleteLines = [];
    private readonly List<CoachRoundResult> _coaches = [];
    private readonly List<EntryRoundResult> _entries = [];
    private readonly List<EntrySlotResult> _entrySlots = [];
    private readonly List<PositionAverage> _averages = [];
    private readonly List<AssetPriceChange> _prices = [];

    private RoundCalculation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public Guid RoundId { get; private set; }

    /// <summary>1 na primeira publicação; cada correção republicada soma um.</summary>
    public int Revision { get; private set; }

    public Modality Modality { get; private set; }

    /// <summary>Versão do <see cref="ScoringRuleSet"/> usada, para reproduzir a conta depois.</summary>
    public int ScoringRuleSetVersion { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    public Guid CalculatedBy { get; private set; }

    public IReadOnlyList<AthleteRoundResult> Athletes => _athletes;

    public IReadOnlyList<AthleteScoreLine> AthleteLines => _athleteLines;

    public IReadOnlyList<CoachRoundResult> Coaches => _coaches;

    public IReadOnlyList<EntryRoundResult> Entries => _entries;

    public IReadOnlyList<EntrySlotResult> EntrySlots => _entrySlots;

    public IReadOnlyList<PositionAverage> Averages => _averages;

    public IReadOnlyList<AssetPriceChange> Prices => _prices;

    public static RoundCalculation Compute(
        Guid id,
        Guid competitionId,
        Guid roundId,
        int revision,
        ScoringRuleSet rules,
        IEnumerable<MatchPerformance> performances,
        IReadOnlyCollection<PricedAsset> catalog,
        IEnumerable<EntryLineup> lineups,
        DateTimeOffset calculatedAt,
        Guid calculatedBy)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(performances);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lineups);
        if (id == Guid.Empty || competitionId == Guid.Empty || roundId == Guid.Empty || calculatedBy == Guid.Empty)
        {
            throw new ArgumentException("Apuração, campeonato, rodada e autor precisam ser identificados.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        var calculation = new RoundCalculation
        {
            Id = id,
            CompetitionId = competitionId,
            RoundId = roundId,
            Revision = revision,
            Modality = rules.Modality,
            ScoringRuleSetVersion = rules.Version,
            CalculatedAt = calculatedAt,
            CalculatedBy = calculatedBy,
        };

        var athletes = AthleteScoring.Score(rules, performances);
        var coachPoints = AthleteScoring.CoachPoints(athletes.Values);
        foreach (var athlete in athletes.Values.OrderBy(item => item.AthleteId))
        {
            calculation._athletes.Add(new(id, athlete));
            calculation._athleteLines.AddRange(
                athlete.Lines.Select(line => new AthleteScoreLine(id, athlete.AthleteId, line)));
        }

        foreach (var coach in catalog.Where(asset => asset.Kind == AssetKind.Coach).OrderBy(asset => asset.AssetId))
        {
            calculation._coaches.Add(new(
                id,
                coach.AssetId,
                coach.RealTeamId,
                coachPoints.ContainsKey(coach.RealTeamId),
                coachPoints.GetValueOrDefault(coach.RealTeamId)));
        }

        foreach (var entry in lineups.OrderBy(item => item.EntryId))
        {
            var score = LineupScoring.Score(rules, entry.Lineup, athletes, coachPoints);
            calculation._entries.Add(new(id, entry.EntryId, score.Total, score.CaptainBonus));
            calculation._entrySlots.AddRange(score.Lines.Select(line => new EntrySlotResult(id, entry.EntryId, line)));
        }

        // Valorização: todo ativo do catálogo tem uma linha, mesmo sem jogar, para que o
        // preço atual de qualquer ativo seja o da última apuração.
        List<ValuationInput> valuation =
        [
            .. catalog.OrderBy(asset => asset.Kind).ThenBy(asset => asset.AssetId).Select(asset =>
                asset.Kind == AssetKind.Coach
                    ? new ValuationInput(
                        asset.Kind,
                        asset.AssetId,
                        null,
                        coachPoints.ContainsKey(asset.RealTeamId),
                        coachPoints.GetValueOrDefault(asset.RealTeamId),
                        asset.Price)
                    : new ValuationInput(
                        asset.Kind,
                        asset.AssetId,
                        asset.Position,
                        athletes.TryGetValue(asset.AssetId, out var score) && score.Played,
                        score?.Points ?? 0m,
                        asset.Price)),
        ];
        foreach (var (group, average) in Valuation.Averages(valuation)
            .OrderBy(pair => pair.Key.Kind)
            .ThenBy(pair => pair.Key.Position))
        {
            calculation._averages.Add(new(id, group.Kind, group.Position, average));
        }

        calculation._prices.AddRange(
            Valuation.Apply(rules, valuation).Select(change => new AssetPriceChange(id, change)));
        return calculation;
    }
}

/// <summary>A rodada de um atleta: jogou ou não e quanto somou nas partidas.</summary>
public sealed class AthleteRoundResult
{
    private AthleteRoundResult()
    {
    }

    internal AthleteRoundResult(Guid calculationId, AthleteRoundScore score)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        AthleteId = score.AthleteId;
        RealTeamId = score.RealTeamId;
        Position = score.Position;
        Played = score.Played;
        Points = score.Points;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public Guid AthleteId { get; private set; }

    public Guid RealTeamId { get; private set; }

    public Position Position { get; private set; }

    public bool Played { get; private set; }

    public decimal Points { get; private set; }
}

/// <summary>
/// Uma linha do extrato de um atleta: numa partida, tal item, tantas vezes, tantos pontos.
/// É a cópia dos fatos da súmula que a apuração usou; corrigir a súmula depois não a muda.
/// </summary>
public sealed class AthleteScoreLine
{
    private AthleteScoreLine()
    {
    }

    internal AthleteScoreLine(Guid calculationId, Guid athleteId, ScoringLine line)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        AthleteId = athleteId;
        MatchId = line.MatchId;
        Item = line.Item;
        Quantity = line.Quantity;
        Points = line.Points;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public Guid AthleteId { get; private set; }

    public Guid MatchId { get; private set; }

    public ScoringItem Item { get; private set; }

    public int Quantity { get; private set; }

    public decimal Points { get; private set; }
}

/// <summary>O técnico na rodada: a média do time, ou nada se ninguém do time jogou.</summary>
public sealed class CoachRoundResult
{
    private CoachRoundResult()
    {
    }

    internal CoachRoundResult(Guid calculationId, Guid coachId, Guid realTeamId, bool played, decimal points)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        CoachId = coachId;
        RealTeamId = realTeamId;
        Played = played;
        Points = points;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public Guid CoachId { get; private set; }

    public Guid RealTeamId { get; private set; }

    public bool Played { get; private set; }

    public decimal Points { get; private set; }
}

/// <summary>O total de uma participação na rodada, com o que o capitão acrescentou.</summary>
public sealed class EntryRoundResult
{
    private EntryRoundResult()
    {
    }

    internal EntryRoundResult(Guid calculationId, Guid entryId, decimal total, decimal captainBonus)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        EntryId = entryId;
        Total = total;
        CaptainBonus = captainBonus;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public Guid EntryId { get; private set; }

    public decimal Total { get; private set; }

    public decimal CaptainBonus { get; private set; }
}

/// <summary>Uma vaga da participação na rodada: quanto fez, se contou e quem cobriu quem.</summary>
public sealed class EntrySlotResult
{
    private EntrySlotResult()
    {
    }

    internal EntrySlotResult(Guid calculationId, Guid entryId, LineupScoreLine line)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        EntryId = entryId;
        Kind = line.Kind;
        AssetId = line.AssetId;
        Role = line.Role;
        Position = line.Position;
        Played = line.Played;
        Points = line.Points;
        Counts = line.Counts;
        Replaces = line.Replaces;
        ReplacedBy = line.ReplacedBy;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public Guid EntryId { get; private set; }

    public AssetKind Kind { get; private set; }

    public Guid AssetId { get; private set; }

    public SquadRole Role { get; private set; }

    public Position? Position { get; private set; }

    public bool Played { get; private set; }

    public decimal Points { get; private set; }

    public bool Counts { get; private set; }

    public Guid? Replaces { get; private set; }

    public Guid? ReplacedBy { get; private set; }
}

/// <summary>A média arredondada de uma posição na rodada, que explica a variação de preço.</summary>
public sealed class PositionAverage
{
    private PositionAverage()
    {
    }

    internal PositionAverage(Guid calculationId, AssetKind kind, Position? position, decimal average)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        Kind = kind;
        Position = position;
        Average = average;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public AssetKind Kind { get; private set; }

    /// <summary>Nula para os técnicos, que formam uma posição própria.</summary>
    public Position? Position { get; private set; }

    public decimal Average { get; private set; }
}

/// <summary>
/// O preço de um ativo depois da rodada. A linha da apuração mais recente é o preço atual
/// do ativo em todo o campeonato: mercado, venda e patrimônio.
/// </summary>
public sealed class AssetPriceChange
{
    private AssetPriceChange()
    {
    }

    internal AssetPriceChange(Guid calculationId, PriceChange change)
    {
        Id = Guid.CreateVersion7();
        CalculationId = calculationId;
        Kind = change.Kind;
        AssetId = change.AssetId;
        PreviousPrice = change.PreviousPrice;
        Average = change.Average;
        Difference = change.Difference;
        Variation = change.Variation;
        NewPrice = change.NewPrice;
    }

    public Guid Id { get; private set; }

    public Guid CalculationId { get; private set; }

    public AssetKind Kind { get; private set; }

    public Guid AssetId { get; private set; }

    public decimal PreviousPrice { get; private set; }

    public decimal? Average { get; private set; }

    public decimal? Difference { get; private set; }

    public decimal Variation { get; private set; }

    public decimal NewPrice { get; private set; }
}
