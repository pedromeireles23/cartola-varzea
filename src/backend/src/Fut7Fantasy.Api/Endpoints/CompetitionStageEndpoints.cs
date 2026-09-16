using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Fases do campeonato: grupos e mata-mata, em ordem livre.</summary>
public static class CompetitionStageEndpoints
{
    /// <summary>Código estável de quem tenta passar do limite de fases.</summary>
    public const string StageLimitCode = "competition_stage_limit";

    public static IEndpointRouteBuilder MapCompetitionStageEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var stages = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/stages")
            .WithTags("Fases do campeonato");

        stages.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ListCompetitionStages")
            .WithSummary("Fases do campeonato em ordem, com grupos e critérios de desempate.");
        stages.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("CreateCompetitionStage")
            .WithSummary("Acrescenta uma fase ao fim da ordem.");
        stages.MapPut("/{stageId:guid}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateCompetitionStage")
            .WithSummary("Substitui a fase; exige a versão lida.");
        stages.MapDelete("/{stageId:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("DeleteCompetitionStage")
            .WithSummary("Remove a fase e renumera as seguintes.");
        stages.MapPut("/order", ReorderAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("ReorderCompetitionStages")
            .WithSummary("Define a ordem das fases; a lista precisa ter exatamente as fases atuais.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid competitionId,
        ICompetitionStageService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(competitionId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(
        Guid competitionId,
        StageRequest request,
        ICompetitionStageService service,
        CancellationToken cancellationToken)
    {
        if (ToDefinition(request, useDefaultTiebreakers: true) is not { } definition)
        {
            return FormatProblem();
        }

        var result = await service.CreateAsync(competitionId, definition, cancellationToken).ConfigureAwait(false);
        return result.Outcome == StageCommandOutcome.Completed
            ? Results.Created($"/api/v1/competitions/{competitionId}/stages/{result.Stage!.Id}", result.Stage)
            : ToResult(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid competitionId,
        Guid stageId,
        StageRequest request,
        ICompetitionStageService service,
        CancellationToken cancellationToken)
    {
        // A edição substitui a fase inteira; critérios ausentes não viram o padrão sem aviso.
        if (ToDefinition(request, useDefaultTiebreakers: false) is not { } definition)
        {
            return FormatProblem();
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return DomainRequests.ValidationProblem([new(nameof(StageRequest.Version), "Informe a versão lida.")]);
        }

        return ToResult(await service.UpdateAsync(
            competitionId, stageId, definition, request.Version, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteAsync(
        Guid competitionId,
        Guid stageId,
        ICompetitionStageService service,
        CancellationToken cancellationToken) =>
        await service.DeleteAsync(competitionId, stageId, cancellationToken).ConfigureAwait(false) switch
        {
            StageCommandOutcome.Completed => Results.NoContent(),
            _ => Results.NotFound(),
        };

    private static async Task<IResult> ReorderAsync(
        Guid competitionId,
        ReorderStagesRequest request,
        ICompetitionStageService service,
        CancellationToken cancellationToken)
    {
        if (request.StageIds is null)
        {
            return DomainRequests.ValidationProblem(
                [new(nameof(ReorderStagesRequest.StageIds), "Informe a ordem das fases.")]);
        }

        var result = await service.ReorderAsync(competitionId, request.StageIds, cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome == StageCommandOutcome.Completed
            ? Results.Ok(result.Stages)
            : StageListChanged();
    }

    private static StageDefinition? ToDefinition(StageRequest request, bool useDefaultTiebreakers)
    {
        if (!DomainRequests.TryParseName<StageFormat>(request.Format, out var format))
        {
            return null;
        }

        var groups = (request.Groups ?? []).Select(group => new GroupDefinition(group.Id, group.Name ?? string.Empty));
        IReadOnlyList<TiebreakCriterion> tiebreakers = request.Tiebreakers is null
            ? useDefaultTiebreakers && format == StageFormat.Groups ? StageDefinition.DefaultTiebreakers : []
            : [.. request.Tiebreakers.Select(name =>
                DomainRequests.TryParseName<TiebreakCriterion>(name, out var criterion) ? criterion : 0)];

        return new StageDefinition(request.Name ?? string.Empty, format, [.. groups], tiebreakers);
    }

    private static IResult FormatProblem() =>
        DomainRequests.ValidationProblem(
            [new(nameof(StageRequest.Format), "Escolha fase de grupos ou mata-mata.")]);

    private static IResult StageListChanged() => Results.Problem(
        title: "Fases alteradas por outra pessoa",
        detail: "Atualize a página para ver as fases atuais antes de continuar.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult ToResult(StageCommandResult result) => result.Outcome switch
    {
        StageCommandOutcome.Completed => Results.Ok(result.Stage),
        StageCommandOutcome.Invalid => DomainRequests.ValidationProblem(result.Errors),
        StageCommandOutcome.NotFound => Results.NotFound(),
        StageCommandOutcome.LimitReached => Results.Problem(
            title: "Limite de fases",
            detail: $"Um campeonato tem no máximo {StageDefinition.MaxStagesPerCompetition} fases.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = StageLimitCode }),
        _ => StageListChanged(),
    };
}

/// <summary>Fase pedida; <c>Version</c> só é exigida na edição.</summary>
public sealed record StageRequest(
    string? Name,
    string? Format,
    IReadOnlyList<StageGroupRequest>? Groups,
    IReadOnlyList<string>? Tiebreakers,
    string? Version);

public sealed record StageGroupRequest(Guid? Id, string? Name);

public sealed record ReorderStagesRequest(IReadOnlyList<Guid>? StageIds);
