using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Fantasy;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Fantasy;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>
/// O jogo do participante: adesão, mercado e escalação (Fase 9).
///
/// O campeonato vai pelo endereço público, que é o que o participante tem na mão. Toda
/// escrita exige sessão fora do modo demonstração, e o mercado aberto é decidido pelo
/// relógio do servidor, nunca pela tela.
/// </summary>
public static class FantasyEndpoints
{
    public const string MarketClosedCode = "fantasy_market_closed";

    public const string NotJoinedCode = "fantasy_not_joined";

    public const string ConflictCode = "fantasy_conflict";

    public static IEndpointRouteBuilder MapFantasyEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var fantasy = routes.MapGroup("/api/v1/fantasy/{slug}").WithTags("Fantasy");

        fantasy.MapGet("/", OverviewAsync)
            .RequireAuthorization()
            .WithName("GetFantasyOverview")
            .WithSummary("Estado do mercado, regras vigentes e o elenco da conta, se ela aderiu.");
        fantasy.MapPost("/entry", JoinAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("JoinFantasy")
            .WithSummary("Adere ao campeonato e recebe o orçamento da modalidade.");
        fantasy.MapGet("/market", MarketAsync)
            .RequireAuthorization()
            .WithName("GetFantasyMarket")
            .WithSummary("Ativos à venda com preço atual e, para cada um, o motivo de um bloqueio.");
        fantasy.MapGet("/rounds", RoundsAsync)
            .RequireAuthorization()
            .WithName("ListFantasyRounds")
            .WithSummary("Rodadas já publicadas, com a pontuação da conta em cada uma.");
        fantasy.MapGet("/rounds/{roundId:guid}", RoundScoreAsync)
            .RequireAuthorization()
            .WithName("GetFantasyRoundScore")
            .WithSummary("Detalhamento da rodada: cada vaga, os eventos que pontuaram e a variação de preço.");
        fantasy.MapPost("/squad/{kind}/{assetId:guid}", BuyAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("BuyFantasyAsset")
            .WithSummary("Compra o ativo e o coloca na primeira vaga que as regras permitem.");
        fantasy.MapDelete("/squad/{kind}/{assetId:guid}", SellAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("SellFantasyAsset")
            .WithSummary("Vende o ativo pelo preço atual e libera a vaga.");
        fantasy.MapPut("/lineup/swap", SwapAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("SwapFantasyStarter")
            .WithSummary("Troca um titular com o reserva da mesma posição.");
        fantasy.MapPut("/lineup/captain", CaptainAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("SetFantasyCaptain")
            .WithSummary("Escolhe o capitão entre os titulares.");

        return routes;
    }

    private static async Task<IResult> OverviewAsync(
        string slug,
        IFantasyService service,
        CancellationToken cancellationToken) =>
        await service.OverviewAsync(slug, cancellationToken).ConfigureAwait(false) is { } overview
            ? Results.Ok(overview)
            : Results.NotFound();

    private static async Task<IResult> MarketAsync(
        string slug,
        IFantasyService service,
        CancellationToken cancellationToken) =>
        await service.MarketAsync(slug, cancellationToken).ConfigureAwait(false) is { } market
            ? Results.Ok(market)
            : Results.NotFound();

    private static async Task<IResult> RoundsAsync(
        string slug,
        IFantasyScoreService service,
        CancellationToken cancellationToken) =>
        await service.RoundsAsync(slug, cancellationToken).ConfigureAwait(false) is { } rounds
            ? Results.Ok(rounds)
            : Results.NotFound();

    private static async Task<IResult> RoundScoreAsync(
        string slug,
        Guid roundId,
        IFantasyScoreService service,
        CancellationToken cancellationToken) =>
        await service.RoundAsync(slug, roundId, cancellationToken).ConfigureAwait(false) is { } score
            ? Results.Ok(score)
            : Results.NotFound();

    private static async Task<IResult> JoinAsync(
        string slug,
        IFantasyService service,
        CancellationToken cancellationToken) =>
        Respond(await service.JoinAsync(slug, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> BuyAsync(
        string slug,
        string kind,
        Guid assetId,
        IFantasyService service,
        CancellationToken cancellationToken) =>
        TryParseKind(kind, out var parsed)
            ? Respond(await service.BuyAsync(slug, parsed, assetId, cancellationToken).ConfigureAwait(false))
            : Results.NotFound();

    private static async Task<IResult> SellAsync(
        string slug,
        string kind,
        Guid assetId,
        IFantasyService service,
        CancellationToken cancellationToken) =>
        TryParseKind(kind, out var parsed)
            ? Respond(await service.SellAsync(slug, parsed, assetId, cancellationToken).ConfigureAwait(false))
            : Results.NotFound();

    private static async Task<IResult> SwapAsync(
        string slug,
        SwapRequest request,
        IFantasyService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Respond(await service
            .SwapAsync(slug, request.StarterAthleteId, request.BenchAthleteId, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> CaptainAsync(
        string slug,
        CaptainRequest request,
        IFantasyService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Respond(await service.SetCaptainAsync(slug, request.AthleteId, cancellationToken).ConfigureAwait(false));
    }

    private static IResult Respond(FantasyCommandResult result) => result.Outcome switch
    {
        FantasyCommandOutcome.Completed => Results.Ok(result.Overview),
        FantasyCommandOutcome.NotFound => Results.NotFound(),
        FantasyCommandOutcome.NotJoined => Conflict(
            "Participação necessária", "Entre no campeonato antes de montar o elenco.", NotJoinedCode),
        FantasyCommandOutcome.MarketClosed => Conflict(
            "Mercado fechado",
            "O mercado está fechado. O elenco só muda enquanto uma rodada está com o mercado aberto.",
            MarketClosedCode),
        FantasyCommandOutcome.Rejected => Conflict(
            "Operação recusada", result.Rejection!.Message, $"fantasy_{result.Rejection.Code}"),
        _ => Conflict(
            "Elenco alterado ao mesmo tempo",
            "Outra ação no seu elenco foi gravada antes desta. Atualize e tente de novo.",
            ConflictCode),
    };

    private static IResult Conflict(string title, string detail, string code) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>As rotas usam o nome em português, como o resto da interface.</summary>
    private static bool TryParseKind(string kind, out AssetKind parsed)
    {
        parsed = kind switch
        {
            "atleta" => AssetKind.Athlete,
            "tecnico" => AssetKind.Coach,
            _ => default,
        };
        return parsed != default;
    }
}

public sealed record SwapRequest(Guid StarterAthleteId, Guid BenchAthleteId);

public sealed record CaptainRequest(Guid AthleteId);
