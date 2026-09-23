using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Fantasy;

namespace Fut7Fantasy.Application.Fantasy;

/// <summary>
/// O jogo do participante num campeonato publicado: adesão, mercado e escalação (01 §9).
///
/// O campeonato é apontado pelo endereço público, que é o que o participante conhece;
/// campeonato em rascunho responde como inexistente. A conta é sempre a da sessão.
/// </summary>
public interface IFantasyService
{
    /// <summary>Campeonatos publicados dos quais a conta participa, para a porta de entrada do jogo.</summary>
    Task<IReadOnlyList<MyFantasyCompetitionView>> MyCompetitionsAsync(CancellationToken cancellationToken);

    Task<FantasyOverview?> OverviewAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Adere ao campeonato. Aderir de novo devolve a participação que já existe.</summary>
    Task<FantasyCommandResult> JoinAsync(string slug, CancellationToken cancellationToken);

    Task<MarketView?> MarketAsync(string slug, CancellationToken cancellationToken);

    Task<FantasyCommandResult> BuyAsync(
        string slug,
        AssetKind kind,
        Guid assetId,
        CancellationToken cancellationToken);

    Task<FantasyCommandResult> SellAsync(
        string slug,
        AssetKind kind,
        Guid assetId,
        CancellationToken cancellationToken);

    Task<FantasyCommandResult> SwapAsync(
        string slug,
        Guid starterAthleteId,
        Guid benchAthleteId,
        CancellationToken cancellationToken);

    Task<FantasyCommandResult> SetCaptainAsync(
        string slug,
        Guid athleteId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resumo leve da participação para o início autenticado. Não carrega catálogo nem pontuação:
/// responde somente qual jogo continuar, quanto do elenco já foi montado e o prazo atual.
/// </summary>
public sealed record MyFantasyCompetitionView(
    string CompetitionName,
    string Slug,
    string Season,
    string Modality,
    decimal Balance,
    int SquadSize,
    int SquadSizeTarget,
    bool HasCaptain,
    DateTimeOffset JoinedAt,
    FantasyMarketStatus Market);

public enum FantasyCommandOutcome
{
    Completed,
    NotFound,

    /// <summary>A conta ainda não aderiu ao campeonato.</summary>
    NotJoined,

    /// <summary>Nenhuma rodada com mercado aberto agora, pelo relógio do servidor.</summary>
    MarketClosed,

    /// <summary>A regra do elenco recusou; o motivo vem em <see cref="FantasyCommandResult.Rejection"/>.</summary>
    Rejected,

    /// <summary>Outra requisição mexeu no elenco ao mesmo tempo; nada foi gravado.</summary>
    Conflict,
}

public sealed record FantasyCommandResult(
    FantasyCommandOutcome Outcome,
    FantasyOverview? Overview,
    SquadRejection? Rejection)
{
    public static FantasyCommandResult Of(FantasyCommandOutcome outcome) => new(outcome, null, null);
}

/// <summary>
/// Tudo o que a tela de jogo precisa para desenhar o campo e o estado do mercado.
/// <see cref="Entry"/> é nula enquanto a conta não aderiu.
/// </summary>
public sealed record FantasyOverview(
    string CompetitionName,
    string Slug,
    ModalityProfileView Profile,
    FantasyMarketStatus Market,
    FantasyTeamLimitView TeamLimit,
    FantasyEntryView? Entry);

/// <summary>
/// Estado do mercado pelo relógio do servidor. <see cref="ClosesAt"/> é o fechamento em
/// UTC, para a contagem regressiva; <see cref="ClosesAtLocal"/>, o mesmo instante no fuso
/// do campeonato (<see cref="TimeZoneId"/>), para o horário absoluto.
/// </summary>
public sealed record FantasyMarketStatus(
    bool IsOpen,
    string? RoundName,
    DateTimeOffset? ClosesAt,
    string? ClosesAtLocal,
    string TimeZoneId);

/// <summary>Limite por time real vigente e quantos times seguem ativos, de onde ele sai.</summary>
public sealed record FantasyTeamLimitView(int ActiveRealTeams, int MaxStarters, int MaxAthletes);

/// <summary>
/// A participação da conta. <see cref="Patrimony"/> é o saldo mais o preço atual do
/// elenco; <see cref="Issues"/> vazia quer dizer escalação completa.
/// <see cref="LastClosedRound"/> diz o que valeu na rodada mais recente cujo mercado já
/// fechou, e é nula enquanto nenhum mercado fechou.
/// </summary>
public sealed record FantasyEntryView(
    decimal Balance,
    decimal Patrimony,
    Guid? CaptainAthleteId,
    IReadOnlyList<SquadSlotView> Slots,
    IReadOnlyList<LineupIssue> Issues,
    ClosedRoundLineupView? LastClosedRound);

/// <summary>
/// A escalação da conta na rodada mais recente com o mercado fechado. <see cref="Status"/>
/// é <c>Frozen</c> (a escalação completa virou retrato e vale para a rodada),
/// <c>Incomplete</c> (faltava algo no fechamento, então a conta não joga a rodada) ou
/// <c>JoinedAfterClose</c> (a conta entrou depois do fechamento e joga a partir da
/// próxima). <see cref="Slots"/> é o retrato, com os nomes do fechamento, e só vem
/// preenchida em <c>Frozen</c>.
/// </summary>
public sealed record ClosedRoundLineupView(
    string RoundName,
    DateTimeOffset MarketClosedAt,
    string MarketClosedAtLocal,
    string Status,
    Guid? CaptainAthleteId,
    IReadOnlyList<FrozenSlotView> Slots);

/// <summary>Vaga congelada no retrato: nome, time e preço como estavam no fechamento.</summary>
public sealed record FrozenSlotView(
    string Kind,
    Guid AssetId,
    string Name,
    string? Position,
    Guid RealTeamId,
    string RealTeamName,
    string Role,
    decimal Price,
    bool IsCaptain);

/// <summary>Vaga ocupada: `Kind` é `Athlete` ou `Coach`; `Role`, `Starter`, `Bench` ou `Coach`.</summary>
public sealed record SquadSlotView(
    string Kind,
    Guid AssetId,
    string Name,
    string? Position,
    Guid RealTeamId,
    string RealTeamName,
    string Role,
    decimal CurrentPrice,
    decimal PurchasePrice,
    bool IsAvailable,
    bool IsCaptain);

/// <summary>Mercado com o saldo da conta, nulo quando ela ainda não aderiu.</summary>
public sealed record MarketView(
    decimal? Balance,
    FantasyMarketStatus Market,
    IReadOnlyList<MarketItemView> Items);

/// <summary>
/// Ativo à venda. <see cref="BlockCode"/> e <see cref="BlockReason"/> dizem por que não
/// dá para comprar agora, e ficam nulos quando dá.
/// </summary>
public sealed record MarketItemView(
    string Kind,
    Guid Id,
    string Name,
    string? Position,
    Guid RealTeamId,
    string RealTeamName,
    decimal Price,
    bool IsAvailable,
    bool IsOwned,
    string? BlockCode,
    string? BlockReason);
