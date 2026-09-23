using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Application.Scoring;

/// <summary>
/// As regras de pontuação e valorização como a página pública as mostra (01 §9).
///
/// Não há consulta ao banco aqui: regra é catálogo versionado do domínio, não dado de
/// campeonato. Por isso a rota também não depende de campeonato nenhum — quem quer
/// entender como se pontua não precisa antes escolher onde jogar.
/// </summary>
public static class PublicScoringRules
{
    public static PublicScoringRulesView Build() =>
        new(
            [
                .. Enum.GetValues<Modality>()
                    .Select(modality => Of(
                        ScoringRuleSets.CurrentFor(modality),
                        ModalityProfiles.CurrentFor(modality))),
            ]);

    private static PublicModalityRulesView Of(ScoringRuleSet rules, ModalityProfile profile) =>
        new(
            rules.Modality.ToString(),
            rules.Version,
            ModalityProfileView.From(profile),
            [
                .. Enum.GetValues<Position>()
                    .Select(position => new PublicGoalPointsView(
                        position.ToString(),
                        rules.GoalFor(position))),
            ],
            rules.Assist,
            rules.CleanSheet,
            rules.GoalConceded,
            rules.GoalkeeperSave,
            rules.PenaltySave,
            rules.YellowCard,
            rules.RedCard,
            rules.OwnGoal,
            rules.PenaltyMiss,
            rules.CaptainMultiplier,
            new PublicValuationView(
                [
                    .. rules.Valuation.Falls
                        .OrderBy(band => band.AtMost)
                        .Select(band => new PublicValuationBandView(band.AtMost, band.Variation)),
                ],
                [
                    .. rules.Valuation.Rises
                        .OrderBy(band => band.AtLeast)
                        .Select(band => new PublicValuationBandView(band.AtLeast, band.Variation)),
                ],
                rules.Valuation.Floor,
                rules.Valuation.Ceiling));
}

/// <summary>As três modalidades da v1, cada uma com a regra vigente dela.</summary>
public sealed record PublicScoringRulesView(IReadOnlyList<PublicModalityRulesView> Modalities);

/// <summary>
/// Quanto vale cada evento numa modalidade, mais o formato do elenco. Gol, assistência,
/// jogo sem sofrer gol e gol sofrido mudam com a modalidade, porque dependem de quantos
/// gols ela produz; os demais eventos valem o mesmo nas três.
/// </summary>
public sealed record PublicModalityRulesView(
    string Modality,
    int Version,
    ModalityProfileView Profile,
    IReadOnlyList<PublicGoalPointsView> GoalPoints,
    decimal Assist,
    decimal CleanSheet,
    decimal GoalConceded,
    decimal GoalkeeperSave,
    decimal PenaltySave,
    decimal YellowCard,
    decimal RedCard,
    decimal OwnGoal,
    decimal PenaltyMiss,
    decimal CaptainMultiplier,
    PublicValuationView Valuation);

/// <summary>O gol vale pela posição cadastrada, nunca pela função exercida na partida.</summary>
public sealed record PublicGoalPointsView(string Position, decimal Points);

/// <summary>
/// Quanto o preço anda conforme a diferença entre a pontuação e a média da posição.
/// <see cref="Falls"/> vem do pior para o menos ruim, e <see cref="Rises"/> do menor
/// para o maior, que é a ordem em que a tabela se lê.
/// </summary>
public sealed record PublicValuationView(
    IReadOnlyList<PublicValuationBandView> Falls,
    IReadOnlyList<PublicValuationBandView> Rises,
    decimal Floor,
    decimal Ceiling);

/// <summary>Uma faixa: o limite da diferença e quanto o preço anda nela.</summary>
public sealed record PublicValuationBandView(decimal Threshold, decimal Variation);
