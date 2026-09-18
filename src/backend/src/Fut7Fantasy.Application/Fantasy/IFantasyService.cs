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
/// do campeonato, para o horário absoluto.
/// </summary>
public sealed record FantasyMarketStatus(
    bool IsOpen,
    string? RoundName,
    DateTimeOffset? ClosesAt,
    string? ClosesAtLocal);

/// <summary>Limite por time real vigente e quantos times seguem ativos, de onde ele sai.</summary>
public sealed record FantasyTeamLimitView(int ActiveRealTeams, int MaxStarters, int MaxAthletes);

/// <summary>
/// A participação da conta. <see cref="Patrimony"/> é o saldo mais o preço atual do
/// elenco; <see cref="Issues"/> vazia quer dizer escalação completa.
/// </summary>
public sealed record FantasyEntryView(
    decimal Balance,
    decimal Patrimony,
    Guid? CaptainAthleteId,
    IReadOnlyList<SquadSlotView> Slots,
    IReadOnlyList<LineupIssue> Issues);

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
