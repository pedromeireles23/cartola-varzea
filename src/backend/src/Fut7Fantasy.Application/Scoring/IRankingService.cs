namespace Fut7Fantasy.Application.Scoring;

/// <summary>
/// O ranking geral acumulado de um campeonato (01 §9). É conteúdo público (01 §10): o
/// visitante sem conta vê a classificação, com o nome de exibição de quem joga e nada
/// além disso — nunca e-mail nem identificador de conta.
/// </summary>
public interface IRankingService
{
    /// <summary>
    /// A classificação do campeonato publicado; nula quando o slug não existe ou o
    /// campeonato ainda está em rascunho.
    /// </summary>
    Task<RankingView?> GeneralAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>
/// O ranking inteiro. <see cref="Rounds"/> é quantas rodadas já foram apuradas: com zero,
/// <see cref="Entries"/> traz todo mundo empatado em nada, que é a foto correta de um
/// campeonato que ainda não começou a pontuar.
/// </summary>
public sealed record RankingView(
    string CompetitionName,
    int Rounds,
    string? LastRoundName,
    bool Provisional,
    IReadOnlyList<RankingRowView> Entries);

/// <summary>
/// Uma linha da classificação. <see cref="Tied"/> marca quem divide a colocação com
/// outra pessoa, e <see cref="LastRoundPoints"/> é nulo para quem não jogou a última
/// rodada apurada.
/// </summary>
public sealed record RankingRowView(
    int Position,
    bool Tied,
    string DisplayName,
    decimal TotalPoints,
    decimal NetWorth,
    decimal? LastRoundPoints,
    bool IsViewer);
