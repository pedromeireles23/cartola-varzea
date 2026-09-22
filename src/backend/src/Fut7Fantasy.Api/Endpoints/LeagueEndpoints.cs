using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Leagues;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Leagues;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>
/// Ligas privadas do participante (01 §9).
///
/// As rotas ficam sob o slug do campeonato, como o resto do jogo: quem participa nunca
/// vê o identificador interno. A entrada pelo código é a única fora dele, porque quem
/// recebe um código ainda não sabe de que campeonato ele é.
/// </summary>
public static class LeagueEndpoints
{
    /// <summary>Código estável de quem tenta passar do limite de participantes.</summary>
    public const string LeagueLimitCode = "league_member_limit";

    /// <summary>Limite das tentativas de código, exigido pela Fase 11.</summary>
    public const string InviteRateLimitPolicy = "liga-convite";

    /// <summary>
    /// Quem recebeu um código digita uma vez, talvez duas se errar. Dez tentativas a
    /// cada cinco minutos não atrapalham ninguém e tornam a força bruta inútil num
    /// espaço de 10¹⁵ códigos.
    /// </summary>
    public const int InviteAttemptsPerWindow = 10;

    public static readonly TimeSpan InviteWindow = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapLeagueEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var leagues = routes.MapGroup("/api/v1/fantasy/{slug}/leagues").WithTags("Ligas privadas");

        leagues.MapGet("/", MineAsync)
            .RequireAuthorization()
            .WithName("ListMyLeagues")
            .WithSummary("Ligas da conta neste campeonato, com a colocação dela em cada uma.");
        leagues.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("CreateLeague")
            .WithSummary("Cria a liga e coloca quem criou dentro dela; exige já jogar o campeonato.");
        leagues.MapGet("/{leagueId:guid}", GetAsync)
            .RequireAuthorization()
            .WithName("GetLeague")
            .WithSummary("A liga com o ranking dela; quem não é membro recebe 404.");
        leagues.MapPut("/{leagueId:guid}/invite", RotateAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("RotateLeagueInvite")
            .WithSummary("Troca o código da liga por outro, ou fecha para novas entradas.");
        leagues.MapDelete("/{leagueId:guid}/members/{membershipId:guid}", RemoveAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("RemoveLeagueMember")
            .WithSummary("O dono remove um membro; qualquer membro sai sozinho.");
        leagues.MapDelete("/{leagueId:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("DeleteLeague")
            .WithSummary("Apaga a liga e as associações dela; só o dono.");

        routes.MapPost("/api/v1/league-invites/{code}/accept", JoinAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .RequireRateLimiting(InviteRateLimitPolicy)
            .WithTags("Ligas privadas")
            .WithName("AcceptLeagueInvite")
            .WithSummary("Entra na liga pelo código; código inválido e liga fechada respondem igual.");

        return routes;
    }

    private static async Task<IResult> MineAsync(
        string slug,
        ILeagueService service,
        CancellationToken cancellationToken) =>
        await service.MineAsync(slug, cancellationToken).ConfigureAwait(false) is { } leagues
            ? Results.Ok(leagues)
            : Results.NotFound();

    private static async Task<IResult> CreateAsync(
        string slug,
        LeagueRequest request,
        ILeagueService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ToResult(await service
            .CreateAsync(slug, new LeagueDefinition(request.Name ?? string.Empty), cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> GetAsync(
        Guid leagueId,
        ILeagueService service,
        CancellationToken cancellationToken) =>
        await service.GetAsync(leagueId, cancellationToken).ConfigureAwait(false) is { } league
            ? Results.Ok(league)
            : Results.NotFound();

    private static async Task<IResult> JoinAsync(
        string code,
        ILeagueService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.JoinAsync(code, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RotateAsync(
        Guid leagueId,
        LeagueInviteRequest request,
        ILeagueService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service
            .RotateInviteAsync(leagueId, request.Close, request.Version!, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> RemoveAsync(
        Guid leagueId,
        Guid membershipId,
        ILeagueService service,
        CancellationToken cancellationToken) =>
        ToResult(await service
            .RemoveMemberAsync(leagueId, membershipId, cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> DeleteAsync(
        Guid leagueId,
        string? version,
        ILeagueService service,
        CancellationToken cancellationToken)
    {
        if (RequiredVersion(version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service.DeleteAsync(leagueId, version!, cancellationToken).ConfigureAwait(false));
    }

    private static IResult? RequiredVersion(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? DomainRequests.ValidationProblem(
                [new CompetitionSettingsError("Version", "Informe a versão lida.")],
                field => field)
            : null;

    private static IResult ToResult(LeagueCommandResult result) => result.Outcome switch
    {
        LeagueCommandOutcome.Completed => result.League is null
            ? Results.NoContent()
            : Results.Ok(result.League),
        LeagueCommandOutcome.Invalid => DomainRequests.ValidationProblem(
            result.Errors.Select(error => new CompetitionSettingsError(error.Field, error.Message)),
            field => field),
        _ => ToResult(result.Outcome),
    };

    private static IResult ToResult(LeagueCommandOutcome outcome) => outcome switch
    {
        LeagueCommandOutcome.Completed => Results.NoContent(),

        // Quem não é da liga recebe o mesmo que quem procurou uma liga inexistente.
        LeagueCommandOutcome.NotFound => Results.NotFound(),
        LeagueCommandOutcome.Forbidden => Results.Forbid(),
        LeagueCommandOutcome.LimitReached => Results.Problem(
            title: "Liga cheia",
            detail: $"Uma liga privada vai até {PrivateLeague.MaxMembers} participantes.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = LeagueLimitCode }),
        _ => Results.Problem(
            title: "Liga alterada por outra pessoa",
            detail: "Atualize a página para ver a versão atual antes de tentar de novo.",
            statusCode: StatusCodes.Status409Conflict),
    };
}

public sealed record LeagueRequest(string? Name);

/// <summary>Trocar o código, ou fechar a liga para novas entradas.</summary>
public sealed record LeagueInviteRequest(string? Version, bool Close);
