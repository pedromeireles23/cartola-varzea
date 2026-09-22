namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Calendário e súmulas de um campeonato publicado, sem conta (Fase 8).
///
/// A regra que atravessa tudo aqui é uma só: **fato publicado é público, fato em edição
/// não é**. O placar e a súmula aparecem quando a rodada está publicada; enquanto ela
/// está em conferência — ou reaberta para correção — a súmula está sendo escrita, e
/// mostrá-la seria publicar meia edição como se fosse resultado.
/// </summary>
public interface IPublicFixtureService
{
    /// <summary>
    /// O calendário inteiro: rodadas em ordem, cada uma com as partidas por horário.
    /// Nulo quando o campeonato não existe ou não está publicado.
    /// </summary>
    Task<PublicFixturesView?> FixturesAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// A súmula de uma partida. Nula quando o campeonato não existe, a partida não é
    /// dele, ou a rodada ainda não publicou resultado.
    /// </summary>
    Task<PublicMatchView?> MatchAsync(string slug, Guid matchId, CancellationToken cancellationToken);
}

/// <summary>Calendário público do campeonato.</summary>
public sealed record PublicFixturesView(
    string Slug,
    string Name,
    string TimeZoneId,
    IReadOnlyList<PublicRoundView> Rounds);

/// <summary>
/// Uma rodada na visão pública. <see cref="ResultPublished"/> é o que libera placar e
/// súmula; <see cref="UnderCorrection"/> avisa que a liga está refazendo a súmula, e
/// nesse intervalo o placar sai do ar em vez de mostrar uma edição pela metade.
/// </summary>
public sealed record PublicRoundView(
    Guid Id,
    string Name,
    int Sequence,
    string Phase,
    bool ResultPublished,
    bool UnderCorrection,
    bool Provisional,
    IReadOnlyList<PublicFixtureView> Matches);

/// <summary>Uma partida no calendário. Placar só quando a rodada publicou.</summary>
public sealed record PublicFixtureView(
    Guid Id,
    string StageName,
    string HomeTeamName,
    string AwayTeamName,
    DateTimeOffset KickoffAt,
    string KickoffLocal,
    string Status,
    int? HomeScore,
    int? AwayScore,
    bool HasSheet);

/// <summary>
/// A súmula publicada de uma partida, dividida pelos dois times. Só entra aqui o que o
/// 01 §11 considera público de um atleta: nome esportivo, posição e o que ele fez em
/// campo.
/// </summary>
public sealed record PublicMatchView(
    Guid Id,
    string Slug,
    string CompetitionName,
    string RoundName,
    string StageName,
    string HomeTeamName,
    string AwayTeamName,
    int HomeScore,
    int AwayScore,
    string KickoffLocal,
    bool Provisional,
    IReadOnlyList<PublicMatchTeamView> Teams);

/// <summary>Um time na súmula, com quem entrou em campo.</summary>
public sealed record PublicMatchTeamView(
    string Name,
    bool IsHome,
    IReadOnlyList<PublicMatchAthleteView> Athletes);

/// <summary>
/// O que um atleta fez na partida. Os zeros vêm junto de propósito: a tela decide o que
/// vale mostrar, e o servidor não precisa adivinhar isso.
/// </summary>
public sealed record PublicMatchAthleteView(
    string SportingName,
    string Position,
    bool PlayedAsGoalkeeper,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    int OwnGoals,
    int PenaltyMisses,
    int GoalsConceded);
