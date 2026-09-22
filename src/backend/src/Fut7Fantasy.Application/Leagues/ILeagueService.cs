using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Leagues;

namespace Fut7Fantasy.Application.Leagues;

/// <summary>
/// Ligas privadas de um campeonato (01 §9). Tudo aqui é sempre da conta da sessão: não
/// existe rota que leia a liga de outra pessoa, nem que entre em nome dela.
///
/// A liga não muda regra nenhuma de pontuação — ela recorta o mesmo ranking geral entre
/// quem se conhece, usando a mesma ordenação.
/// </summary>
public interface ILeagueService
{
    /// <summary>As ligas da conta naquele campeonato; nula quando o campeonato não existe.</summary>
    Task<IReadOnlyList<LeagueSummaryView>?> MineAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Cria a liga e coloca quem criou dentro dela. Exige que a conta já jogue o
    /// campeonato: liga é um recorte de quem disputa, não uma sala de espectadores.
    /// </summary>
    Task<LeagueCommandResult> CreateAsync(
        string slug,
        LeagueDefinition definition,
        CancellationToken cancellationToken);

    /// <summary>
    /// A liga com o ranking dela. Só quem é membro lê; qualquer outra conta recebe o
    /// mesmo "não encontrado" de uma liga inexistente.
    /// </summary>
    Task<LeagueView?> GetAsync(Guid leagueId, CancellationToken cancellationToken);

    /// <summary>
    /// Entra pelo código. Código errado, vencido, fechado ou de campeonato que a conta
    /// não joga recebem a mesma resposta, para não confirmar que a liga existe.
    /// </summary>
    Task<LeagueCommandResult> JoinAsync(string? code, CancellationToken cancellationToken);

    /// <summary>Troca o código por outro, ou fecha a liga para novas entradas.</summary>
    Task<LeagueCommandResult> RotateInviteAsync(
        Guid leagueId,
        bool close,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tira alguém da liga. O dono remove qualquer membro; qualquer membro sai sozinho.
    /// O dono não sai da própria liga: para isso ele a apaga.
    /// </summary>
    Task<LeagueCommandOutcome> RemoveMemberAsync(
        Guid leagueId,
        Guid membershipId,
        CancellationToken cancellationToken);

    /// <summary>Apaga a liga e todas as associações. Só o dono.</summary>
    Task<LeagueCommandOutcome> DeleteAsync(Guid leagueId, string version, CancellationToken cancellationToken);
}

public enum LeagueCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,

    /// <summary>Quem pediu não tem o papel exigido naquela liga.</summary>
    Forbidden,

    /// <summary>A liga chegou ao limite de participantes.</summary>
    LimitReached,
}

public sealed record LeagueCommandResult(
    LeagueCommandOutcome Outcome,
    LeagueSummaryView? League,
    IReadOnlyList<LeagueError> Errors)
{
    public static LeagueCommandResult Of(LeagueCommandOutcome outcome) => new(outcome, null, []);

    public static LeagueCommandResult Invalid(params LeagueError[] errors) =>
        new(LeagueCommandOutcome.Invalid, null, errors);
}

/// <summary>
/// Uma liga na lista da conta. <see cref="InviteCode"/> só vem para o dono: é ele quem
/// reenvia o código ao grupo.
/// </summary>
public sealed record LeagueSummaryView(
    Guid Id,
    string Name,
    string CompetitionSlug,
    int Members,
    bool IsOwner,
    int? Position,
    string? InviteCode,
    string? InviteExpiresAtLocal,
    string Version);

/// <summary>A liga aberta, com o ranking dela e a lista de quem está dentro.</summary>
public sealed record LeagueView(
    Guid Id,
    string Name,
    string CompetitionName,
    string CompetitionSlug,
    bool IsOwner,
    string? InviteCode,
    string? InviteExpiresAtLocal,
    int Rounds,
    string? LastRoundName,
    bool Provisional,
    IReadOnlyList<LeagueMemberView> Members,
    string Version);

/// <summary>
/// Uma linha do ranking da liga. <see cref="MembershipId"/> é o que a remoção usa, e vem
/// só para o dono e para a própria conta — é quem pode agir naquela linha.
/// </summary>
public sealed record LeagueMemberView(
    Guid? MembershipId,
    int Position,
    bool Tied,
    string DisplayName,
    decimal TotalPoints,
    decimal NetWorth,
    decimal? LastRoundPoints,
    bool IsViewer,
    bool IsOwner);
