using System.Collections.Frozen;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>
/// Quanto vale cada evento da súmula numa modalidade (01 §9, "Pontuação v1").
///
/// É versionado como o perfil da modalidade (ADR-008): recalibrar gera uma versão nova,
/// nunca edita a existente, e a apuração guarda a versão que usou, para que uma rodada
/// já apurada não mude por acidente quando a regra mudar.
/// </summary>
public sealed class ScoringRuleSet
{
    /// <summary>
    /// Casas decimais de todo valor derivado da apuração — a média do técnico e a média da
    /// posição. Eventos e preços já são múltiplos de 0,5; só as médias precisam de corte.
    /// </summary>
    public const int Decimals = 2;

    private readonly FrozenDictionary<Position, decimal> _goalPoints;

    internal ScoringRuleSet(
        Modality modality,
        int version,
        IReadOnlyDictionary<Position, decimal> goalPoints,
        decimal assist,
        decimal cleanSheet,
        decimal goalConceded,
        CommonEventPoints common)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A versão começa em 1.");
        }

        if (Enum.GetValues<Position>().Any(position => !goalPoints.ContainsKey(position)))
        {
            throw new ArgumentException("Toda posição precisa do valor do gol.", nameof(goalPoints));
        }

        Modality = modality;
        Version = version;
        _goalPoints = goalPoints.ToFrozenDictionary();
        Assist = assist;
        CleanSheet = cleanSheet;
        GoalConceded = goalConceded;
        PenaltySave = common.PenaltySave;
        GoalkeeperSave = common.GoalkeeperSave;
        YellowCard = common.YellowCard;
        RedCard = common.RedCard;
        OwnGoal = common.OwnGoal;
        PenaltyMiss = common.PenaltyMiss;
        CaptainMultiplier = common.CaptainMultiplier;
        Valuation = common.Valuation;
    }

    public Modality Modality { get; }

    public int Version { get; }

    public decimal Assist { get; }

    /// <summary>Jogo sem sofrer gol, para goleiro ou defensor elegível.</summary>
    public decimal CleanSheet { get; }

    /// <summary>Por gol sofrido, registrado para o goleiro que atuou; é negativo.</summary>
    public decimal GoalConceded { get; }

    public decimal PenaltySave { get; }

    public decimal GoalkeeperSave { get; }

    /// <summary>Só o primeiro amarelo da partida: o segundo vira vermelho (01 §9).</summary>
    public decimal YellowCard { get; }

    public decimal RedCard { get; }

    public decimal OwnGoal { get; }

    public decimal PenaltyMiss { get; }

    /// <summary>O capitão que jogou multiplica os próprios pontos, positivos ou negativos.</summary>
    public decimal CaptainMultiplier { get; }

    /// <summary>Faixas de valorização, piso e teto de preço (01 §9, "Valorização relativa à posição").</summary>
    public ValuationBands Valuation { get; }

    /// <summary>O gol vale pela posição cadastrada, nunca pela função exercida na partida.</summary>
    public decimal GoalFor(Position position) => _goalPoints[position];

    /// <summary>O arredondamento único da apuração: duas casas, meio para longe de zero.</summary>
    public static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}

/// <summary>Eventos e valorização que valem o mesmo nas três modalidades da v1.</summary>
internal sealed record CommonEventPoints(
    decimal PenaltySave,
    decimal GoalkeeperSave,
    decimal YellowCard,
    decimal RedCard,
    decimal OwnGoal,
    decimal PenaltyMiss,
    decimal CaptainMultiplier,
    ValuationBands Valuation);

/// <summary>
/// Quanto o preço anda conforme a diferença entre a pontuação do ativo e a média da
/// posição. As faixas são semiabertas e não deixam buraco entre si: do lado negativo,
/// vale a primeira em que <c>d ≤ limite</c>; do positivo, a primeira em que
/// <c>d ≥ limite</c>; entre os dois lados, o preço não muda.
/// </summary>
public sealed record ValuationBands(
    IReadOnlyList<(decimal AtMost, decimal Variation)> Falls,
    IReadOnlyList<(decimal AtLeast, decimal Variation)> Rises,
    decimal Floor,
    decimal Ceiling)
{
    /// <summary>Variação de preço para a diferença <paramref name="difference"/>.</summary>
    public decimal VariationFor(decimal difference)
    {
        foreach (var (atMost, variation) in Falls.OrderBy(band => band.AtMost))
        {
            if (difference <= atMost)
            {
                return variation;
            }
        }

        foreach (var (atLeast, variation) in Rises.OrderByDescending(band => band.AtLeast))
        {
            if (difference >= atLeast)
            {
                return variation;
            }
        }

        return 0m;
    }

    /// <summary>Preço novo, sempre entre o piso e o teto.</summary>
    public decimal Apply(decimal price, decimal variation) => Math.Clamp(price + variation, Floor, Ceiling);
}
