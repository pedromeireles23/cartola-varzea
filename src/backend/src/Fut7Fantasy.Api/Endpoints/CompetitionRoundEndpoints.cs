using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Rodadas do campeonato e as partidas dentro delas.</summary>
public static class CompetitionRoundEndpoints
{
    /// <summary>Código estável de quem tenta passar do limite de rodadas.</summary>
    public const string RoundLimitCode = "competition_round_limit";

    /// <summary>Código estável de quem tenta uma operação que o estado da rodada não aceita.</summary>
    public const string RoundStatusCode = "competition_round_status";

    public static IEndpointRouteBuilder MapCompetitionRoundEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var rounds = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/rounds")
            .WithTags("Rodadas do campeonato");

        rounds.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ListCompetitionRounds")
            .WithSummary("Rodadas em ordem, com as partidas e o fechamento do mercado.");
        rounds.MapGet("/{roundId:guid}/review", ReviewAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ReviewCompetitionRound")
            .WithSummary("Consolida súmulas e pendências da rodada para conferência.");
        rounds.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("CreateCompetitionRound")
            .WithSummary("Acrescenta uma rodada em rascunho ao fim da ordem.");
        rounds.MapPut("/{roundId:guid}", RenameAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("RenameCompetitionRound")
            .WithSummary("Renomeia a rodada; exige a versão lida.");
        rounds.MapDelete("/{roundId:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("DeleteCompetitionRound")
            .WithSummary("Remove uma rodada em rascunho, com suas partidas, e renumera as seguintes.");
        rounds.MapPut("/{roundId:guid}/status", ChangeStatusAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("ChangeCompetitionRoundStatus")
            .WithSummary("Abre o mercado, inicia a revisão, volta para rascunho ou cancela a rodada.");
        rounds.MapPost("/{roundId:guid}/publish", PublishAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("PublishCompetitionRound")
            .WithSummary("Apura e publica a rodada em revisão; repetir o pedido não apura de novo.");
        rounds.MapPost("/{roundId:guid}/matches", AddMatchAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("AddCompetitionMatch")
            .WithSummary("Cria uma partida na rodada; exige a versão lida da rodada.");
        rounds.MapPut("/{roundId:guid}/matches/{matchId:guid}", UpdateMatchAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateCompetitionMatch")
            .WithSummary("Reagenda, adia ou cancela a partida; troca times só no rascunho.");
        rounds.MapDelete("/{roundId:guid}/matches/{matchId:guid}", RemoveMatchAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("RemoveCompetitionMatch")
            .WithSummary("Remove a partida da rodada em rascunho.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid competitionId,
        ICompetitionRoundService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(competitionId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ReviewAsync(
        Guid competitionId,
        Guid roundId,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        var review = await service.ReviewAsync(competitionId, roundId, cancellationToken)
            .ConfigureAwait(false);
        return review is null ? Results.NotFound() : Results.Ok(review);
    }

    private static async Task<IResult> CreateAsync(
        Guid competitionId,
        RoundRequest request,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await service
            .CreateAsync(competitionId, new RoundDefinition(request.Name ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome == RoundCommandOutcome.Completed
            ? Results.Created(
                $"/api/v1/competitions/{competitionId}/rounds/{result.Round!.Id}", result.Round)
            : ToResult(result);
    }

    private static async Task<IResult> RenameAsync(
        Guid competitionId,
        Guid roundId,
        RoundRequest request,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service.RenameAsync(
            competitionId,
            roundId,
            new RoundDefinition(request.Name ?? string.Empty),
            request.Version!,
            cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteAsync(
        Guid competitionId,
        Guid roundId,
        ICompetitionRoundService service,
        CancellationToken cancellationToken) =>
        await service.DeleteAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false) switch
        {
            RoundCommandOutcome.Completed => Results.NoContent(),
            RoundCommandOutcome.NotFound => Results.NotFound(),
            _ => StatusProblem("Só uma rodada em rascunho pode ser removida."),
        };

    private static async Task<IResult> ChangeStatusAsync(
        Guid competitionId,
        Guid roundId,
        RoundStatusRequest request,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        if (!Enum.TryParse<RoundTransition>(request.Transition, ignoreCase: true, out var transition)
            || !Enum.IsDefined(transition))
        {
            return DomainRequests.ValidationProblem(
                [new CompetitionSettingsError(
                    nameof(RoundStatusRequest.Transition),
                    "Escolha abrir o mercado, enviar para revisão, voltar para rascunho ou cancelar.")],
                field => field);
        }

        return ToResult(await service.ChangeStatusAsync(
            competitionId, roundId, transition, request.Version!, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> AddMatchAsync(
        Guid competitionId,
        Guid roundId,
        MatchRequest request,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service.AddMatchAsync(
            competitionId, roundId, request.ToInput(), request.Version!, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        MatchRequest request,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service.UpdateMatchAsync(
            competitionId, roundId, matchId, request.ToInput(), request.Version!, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> RemoveMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        string? version,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        if (RequiredVersion(version) is { } missing)
        {
            return missing;
        }

        return ToResult(await service.RemoveMatchAsync(
            competitionId, roundId, matchId, version!, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> PublishAsync(
        Guid competitionId,
        Guid roundId,
        RoundPublicationRequest request,
        IRoundPublicationService publication,
        ICompetitionRoundService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequiredVersion(request.Version) is { } missing)
        {
            return missing;
        }

        var result = await publication
            .PublishAsync(competitionId, roundId, request.Version!, cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome switch
        {
            RoundPublicationOutcome.Completed => Results.Ok(
                await service.ReviewAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false)),
            RoundPublicationOutcome.NotFound => Results.NotFound(),
            RoundPublicationOutcome.Invalid => DomainRequests.ValidationProblem(
                result.Errors.Select(error => new CompetitionSettingsError(error.Field, error.Message)),
                field => field),
            RoundPublicationOutcome.StatusLocked => StatusProblem(
                "Só uma rodada em revisão pode ser publicada. Recarregue para ver a situação."),
            _ => Results.Problem(
                title: "Rodada alterada por outra pessoa",
                detail: "Atualize a página para ver a versão atual antes de publicar.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static IResult? RequiredVersion(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? DomainRequests.ValidationProblem(
                [new CompetitionSettingsError("Version", "Informe a versão lida.")],
                field => field)
            : null;

    private static IResult ToResult(RoundCommandResult result) => result.Outcome switch
    {
        RoundCommandOutcome.Completed => Results.Ok(result.Round),
        RoundCommandOutcome.NotFound => Results.NotFound(),
        RoundCommandOutcome.Invalid => DomainRequests.ValidationProblem(
            result.Errors.Select(error => new CompetitionSettingsError(error.Field, error.Message)),
            field => field),
        RoundCommandOutcome.LimitReached => Results.Problem(
            title: "Limite de rodadas atingido",
            detail: $"Um campeonato tem no máximo {Round.MaxRoundsPerCompetition} rodadas.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = RoundLimitCode }),
        RoundCommandOutcome.StatusLocked => StatusProblem(
            "A rodada está num estado que não aceita essa mudança. Recarregue para ver a situação."),
        _ => Results.Problem(
            title: "Rodada alterada por outra pessoa",
            detail: "Atualize a página para ver a versão atual antes de salvar.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult StatusProblem(string detail) => Results.Problem(
        title: "Operação indisponível neste estado",
        detail: detail,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["code"] = RoundStatusCode });
}

public sealed record RoundRequest(string? Name, string? Version);

/// <summary>A versão da rodada lida na conferência.</summary>
public sealed record RoundPublicationRequest(string? Version);

/// <summary>`OpenMarket`, `ReopenForEditing`, `SendToReview` ou `Cancel`.</summary>
public sealed record RoundStatusRequest(string? Transition, string? Version);

/// <summary>
/// Partida enviada pela tela. O horário vem como texto local (`2026-09-20T15:30`), sem
/// fuso: quem converte é o servidor, pelo fuso do campeonato.
/// </summary>
public sealed record MatchRequest(
    Guid StageId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    string? KickoffLocal,
    string? Status,
    string? Version)
{
    public MatchInput ToInput() => new(StageId, HomeTeamId, AwayTeamId, KickoffLocal, Status);
}
