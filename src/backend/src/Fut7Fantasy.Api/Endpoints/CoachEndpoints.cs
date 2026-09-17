using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Técnico único criado automaticamente para cada time.</summary>
public static class CoachEndpoints
{
    public static IEndpointRouteBuilder MapCoachEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var coaches = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/coaches")
            .WithTags("Catálogo esportivo");

        coaches.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ListCoaches")
            .WithSummary("Técnico de cada time, incluindo os times arquivados.");
        coaches.MapPut("/{coachId:guid}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateCoach")
            .WithSummary("Altera nome opcional e preço do técnico; exige a versão lida.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid competitionId,
        ICoachService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(competitionId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(
        Guid competitionId,
        Guid coachId,
        CoachRequest request,
        ICoachService service,
        CancellationToken cancellationToken)
    {
        if (!DomainRequests.TryParseName<PriceTier>(request.PriceTier, out var priceTier))
        {
            return DomainRequests.SportsCatalogValidationProblem(
                [new(nameof(CoachRequest.PriceTier), "Escolha um nível de preço válido.")]);
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return DomainRequests.SportsCatalogValidationProblem(
                [new(nameof(CoachRequest.Version), "Informe a versão lida.")]);
        }

        var result = await service.UpdateAsync(
            competitionId,
            coachId,
            new CoachDefinition(request.DisplayName, priceTier, request.InitialPriceOverride),
            request.Version,
            cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            CoachCommandOutcome.Completed => Results.Ok(result.Coach),
            CoachCommandOutcome.Invalid => DomainRequests.SportsCatalogValidationProblem(result.Errors),
            CoachCommandOutcome.NotFound => Results.NotFound(),
            _ => Results.Problem(
                title: "Técnico alterado por outra pessoa",
                detail: "Atualize a lista antes de salvar de novo.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "coach_concurrency_conflict" }),
        };
    }
}

public sealed record CoachRequest(
    string? DisplayName,
    string? PriceTier,
    decimal? InitialPriceOverride,
    string? Version);
