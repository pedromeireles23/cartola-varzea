using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

public static class MatchSheetEndpoints
{
    public const string StatusCode = "competition_match_sheet_status";

    public static IEndpointRouteBuilder MapMatchSheetEndpoints(this IEndpointRouteBuilder routes)
    {
        var sheets = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/matches/{matchId:guid}/sheet")
            .WithTags("Súmula da partida");
        sheets.MapGet("/", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("GetCompetitionMatchSheet")
            .WithSummary("Elenco e conteúdo atual da súmula da partida.");
        sheets.MapPut("/", SaveAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionStaffWrite)
            .WithName("SaveCompetitionMatchSheet")
            .WithSummary("Salva placar, participações e eventos objetivos da partida.");
        return routes;
    }

    private static async Task<IResult> GetAsync(
        Guid competitionId,
        Guid matchId,
        IMatchSheetService service,
        CancellationToken cancellationToken) =>
        await service.GetAsync(competitionId, matchId, cancellationToken).ConfigureAwait(false) is { } sheet
            ? Results.Ok(sheet)
            : Results.NotFound();

    private static async Task<IResult> SaveAsync(
        Guid competitionId,
        Guid matchId,
        MatchSheetRequest request,
        IMatchSheetService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = new MatchSheetInput(
            request.HomeScore,
            request.AwayScore,
            [.. (request.Appearances ?? []).Select(item => item.ToInput())],
            request.Version);
        var result = await service.SaveAsync(competitionId, matchId, input, cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome switch
        {
            MatchSheetCommandOutcome.Completed => Results.Ok(result.Sheet),
            MatchSheetCommandOutcome.NotFound => Results.NotFound(),
            MatchSheetCommandOutcome.Invalid => DomainRequests.ValidationProblem(
                result.Errors.Select(error => new CompetitionSettingsError(error.Field, error.Message)),
                field => field),
            MatchSheetCommandOutcome.StatusLocked => Results.Problem(
                title: "Súmula indisponível neste estado",
                detail: "A partida precisa estar em andamento ou em revisão para receber a súmula.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = StatusCode }),
            _ => Results.Problem(
                title: "Súmula alterada por outra pessoa",
                detail: "Atualize a página antes de salvar novamente.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }
}

public sealed record MatchSheetRequest(
    int HomeScore,
    int AwayScore,
    IReadOnlyList<MatchSheetAppearanceRequest>? Appearances,
    string? Version);

public sealed record MatchSheetAppearanceRequest(
    Guid AthleteId,
    bool DidPlay,
    bool PlayedAsGoalkeeper,
    int GoalsConceded,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    string? RedCardReason,
    int OwnGoals,
    int PenaltyMisses)
{
    public MatchSheetAppearanceInput ToInput() => new(
        AthleteId,
        DidPlay,
        PlayedAsGoalkeeper,
        GoalsConceded,
        Goals,
        Assists,
        GoalkeeperSaves,
        PenaltySaves,
        YellowCards,
        RedCards,
        RedCardReason,
        OwnGoals,
        PenaltyMisses);
}
