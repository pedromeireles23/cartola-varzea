namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Time e atleta na visão pública (Fase 8).
///
/// Vale a mesma regra do calendário — fato publicado é público —, então as estatísticas
/// e o histórico de preço só contam rodadas com resultado no ar. E vale o 01 §11: de uma
/// pessoa aparecem nome esportivo, posição, time, preço e o que ela fez em campo. Nada
/// de contato, documento ou data de nascimento, que nem chegam a ser consultados.
/// </summary>
public interface IPublicCatalogService
{
    /// <summary>O time com o elenco e o técnico; nulo quando o time não é do campeonato.</summary>
    Task<PublicTeamDetailView?> TeamAsync(string slug, Guid teamId, CancellationToken cancellationToken);

    /// <summary>O atleta com o que fez e como o preço dele andou.</summary>
    Task<PublicAthleteView?> AthleteAsync(string slug, Guid athleteId, CancellationToken cancellationToken);
}

/// <summary>Um time do campeonato, com quem está inscrito nele.</summary>
public sealed record PublicTeamDetailView(
    Guid Id,
    string Slug,
    string CompetitionName,
    string Name,
    string? CoachName,
    IReadOnlyList<PublicSquadAthleteView> Athletes);

/// <summary>
/// Um atleta no elenco. <see cref="Active"/> é falso para quem foi liberado no meio do
/// campeonato: ele continua no histórico, porque jogou, mas não está mais disponível.
/// </summary>
public sealed record PublicSquadAthleteView(
    Guid Id,
    string SportingName,
    string Position,
    decimal Price,
    bool Active);

/// <summary>O perfil público de um atleta (01 §11).</summary>
public sealed record PublicAthleteView(
    Guid Id,
    string Slug,
    string CompetitionName,
    string SportingName,
    string Position,
    Guid RealTeamId,
    string RealTeamName,
    decimal Price,
    bool Active,
    PublicAthleteTotalsView Totals,
    IReadOnlyList<PublicAthletePriceView> PriceHistory);

/// <summary>
/// O que o atleta fez nas rodadas já publicadas. <see cref="CleanSheets"/> só conta para
/// quem atuou no gol, que é quem a regra premia por não sofrer gol.
/// </summary>
public sealed record PublicAthleteTotalsView(
    int Matches,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    int OwnGoals,
    int PenaltyMisses,
    int CleanSheets);

/// <summary>
/// Como o preço andou numa rodada. Só a revisão vigente de cada rodada entra: uma
/// correção republicada substitui a anterior, e o histórico mostra o que vale agora.
/// </summary>
public sealed record PublicAthletePriceView(
    string RoundName,
    int Sequence,
    decimal PreviousPrice,
    decimal NewPrice,
    decimal Variation);
