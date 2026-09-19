using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Scoring;

/// <summary>O que gerou pontos, para o detalhamento que o participante lê (02 §8, "Score breakdown").</summary>
public enum ScoringItem
{
    Goal = 1,
    Assist = 2,
    GoalkeeperSave = 3,
    PenaltySave = 4,
    YellowCard = 5,
    RedCard = 6,
    OwnGoal = 7,
    PenaltyMiss = 8,
    GoalConceded = 9,
    CleanSheet = 10,
}

/// <summary>
/// Um atleta numa partida da rodada, como a apuração o enxerga: o que a súmula registrou
/// para ele e quantos gols o time dele sofreu no jogo — o placar do adversário, que já
/// inclui gol contra de companheiro.
/// </summary>
public sealed record MatchPerformance(
    Guid MatchId,
    int TeamGoalsConceded,
    MatchSheetAppearanceDefinition Appearance);

/// <summary>Uma linha do detalhamento: numa partida, tal item, tantas vezes, tantos pontos.</summary>
public sealed record ScoringLine(Guid MatchId, ScoringItem Item, int Quantity, decimal Points);

/// <summary>
/// A rodada de um atleta, com as partidas somadas (01 §9). <see cref="Played"/> é ter
/// entrado em campo em ao menos uma partida da rodada: quem não jogou não pontua, não
/// entra na média do técnico nem na da posição, e pode ser coberto pelo banco.
/// </summary>
public sealed record AthleteRoundScore(
    Guid AthleteId,
    Guid RealTeamId,
    Position Position,
    bool Played,
    decimal Points,
    IReadOnlyList<ScoringLine> Lines);

/// <summary>
/// Pontuação dos atletas numa rodada, a partir das súmulas das partidas que valem para
/// ela. Partida adiada ou cancelada depois do fechamento não entra: quem chama só passa
/// as partidas apuradas. Não existe ponto por participação.
/// </summary>
public static class AthleteScoring
{
    public static IReadOnlyDictionary<Guid, AthleteRoundScore> Score(
        ScoringRuleSet rules,
        IEnumerable<MatchPerformance> performances)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(performances);

        return performances
            .GroupBy(performance => performance.Appearance.AthleteId)
            .ToDictionary(
                athlete => athlete.Key,
                athlete =>
                {
                    var first = athlete.First().Appearance;
                    var lines = athlete
                        .Where(performance => performance.Appearance.DidPlay)
                        .SelectMany(performance => LinesFor(rules, performance))
                        .OrderBy(line => line.MatchId)
                        .ThenBy(line => line.Item)
                        .ToList();
                    return new AthleteRoundScore(
                        athlete.Key,
                        first.RealTeamId,
                        first.Position,
                        athlete.Any(performance => performance.Appearance.DidPlay),
                        lines.Sum(line => line.Points),
                        lines);
                });
    }

    /// <summary>
    /// O técnico recebe a média das pontuações de rodada, já somadas entre partidas, dos
    /// atletas do time dele que jogaram. Time sem ninguém em campo fica fora: o técnico
    /// dele não atuou.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> CoachPoints(IEnumerable<AthleteRoundScore> athletes)
    {
        ArgumentNullException.ThrowIfNull(athletes);

        return athletes
            .Where(athlete => athlete.Played)
            .GroupBy(athlete => athlete.RealTeamId)
            .ToDictionary(
                team => team.Key,
                team => ScoringRuleSet.Round(team.Average(athlete => athlete.Points)));
    }

    /// <summary>Os pontos de uma partida, item por item. Quem não jogou não gera linha.</summary>
    public static IEnumerable<ScoringLine> LinesFor(ScoringRuleSet rules, MatchPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(performance);

        var appearance = performance.Appearance;
        if (!appearance.DidPlay)
        {
            yield break;
        }

        var match = performance.MatchId;

        // Segundo amarelo vira vermelho sem somar outro amarelo: dois amarelos valem
        // -2 -5, com ou sem o vermelho lançado na súmula (01 §9, "Cartões").
        var sentOff = appearance.RedCards > 0 || appearance.YellowCards >= 2;

        if (appearance.Goals > 0)
        {
            yield return new(
                match, ScoringItem.Goal, appearance.Goals, appearance.Goals * rules.GoalFor(appearance.Position));
        }

        if (appearance.Assists > 0)
        {
            yield return new(match, ScoringItem.Assist, appearance.Assists, appearance.Assists * rules.Assist);
        }

        if (appearance.GoalkeeperSaves > 0)
        {
            yield return new(
                match,
                ScoringItem.GoalkeeperSave,
                appearance.GoalkeeperSaves,
                appearance.GoalkeeperSaves * rules.GoalkeeperSave);
        }

        if (appearance.PenaltySaves > 0)
        {
            yield return new(
                match, ScoringItem.PenaltySave, appearance.PenaltySaves, appearance.PenaltySaves * rules.PenaltySave);
        }

        if (appearance.YellowCards > 0)
        {
            yield return new(match, ScoringItem.YellowCard, 1, rules.YellowCard);
        }

        if (sentOff)
        {
            yield return new(match, ScoringItem.RedCard, 1, rules.RedCard);
        }

        if (appearance.OwnGoals > 0)
        {
            yield return new(match, ScoringItem.OwnGoal, appearance.OwnGoals, appearance.OwnGoals * rules.OwnGoal);
        }

        if (appearance.PenaltyMisses > 0)
        {
            yield return new(
                match, ScoringItem.PenaltyMiss, appearance.PenaltyMisses, appearance.PenaltyMisses * rules.PenaltyMiss);
        }

        // Vale para quem atuou no gol, qualquer que seja a posição cadastrada.
        if (appearance.GoalsConceded > 0)
        {
            yield return new(
                match,
                ScoringItem.GoalConceded,
                appearance.GoalsConceded,
                appearance.GoalsConceded * rules.GoalConceded);
        }

        // Goleiro ou defensor pela posição cadastrada, time sem gol sofrido — gol contra
        // de companheiro incluído — e sem expulsão própria (01 §9, "Jogo sem sofrer gol").
        if (appearance.Position is Position.Goalkeeper or Position.Defender
            && performance.TeamGoalsConceded == 0
            && !sentOff)
        {
            yield return new(match, ScoringItem.CleanSheet, 1, rules.CleanSheet);
        }
    }
}
