namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Formato de uma fase. O campeonato encadeia fases em qualquer ordem, sem um
/// chaveamento fixo (01 §7).
/// </summary>
public enum StageFormat
{
    /// <summary>Times divididos em grupos com classificação; um grupo só vale pontos corridos.</summary>
    Groups = 1,

    /// <summary>Confrontos eliminatórios, sem classificação por pontos.</summary>
    Knockout = 2,
}

/// <summary>
/// Critério de desempate da classificação, aplicado depois dos pontos (vitória 3, empate
/// 1, derrota 0) na ordem escolhida pela organização.
/// </summary>
public enum TiebreakCriterion
{
    Wins = 1,
    GoalDifference = 2,
    GoalsFor = 3,
    HeadToHead = 4,
    FewestRedCards = 5,
    FewestYellowCards = 6,
}
