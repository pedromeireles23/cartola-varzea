using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Cadastro manual dos times reais do campeonato.</summary>
public static class RealTeamEndpoints
{
    public const string DuplicateNameCode = "real_team_name_duplicate";

    public static IEndpointRouteBuilder MapRealTeamEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var teams = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/teams")
            .WithTags("Catálogo esportivo");

        teams.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ListRealTeams")
            .WithSummary("Times reais do campeonato, incluindo os arquivados.");
        teams.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("CreateRealTeam")
            .WithSummary("Cadastra um time real no campeonato.");
        teams.MapPut("/{teamId:guid}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateRealTeam")
            .WithSummary("Altera o time real; exige a versão lida.");
        teams.MapDelete("/{teamId:guid}", ArchiveAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("ArchiveRealTeam")
            .WithSummary("Arquiva o time real sem apagar seu histórico.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid competitionId,
        IRealTeamService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(competitionId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(
        Guid competitionId,
        RealTeamRequest request,
        IRealTeamService service,
        CancellationToken cancellationToken) =>
        ToResult(
            await service.CreateAsync(
                competitionId,
                new RealTeamDefinition(request.Name ?? string.Empty),
                cancellationToken).ConfigureAwait(false),
            createdAt: competitionId);

    private static async Task<IResult> UpdateAsync(
        Guid competitionId,
        Guid teamId,
        RealTeamRequest request,
        IRealTeamService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return DomainRequests.SportsCatalogValidationProblem(
                [new SportsCatalogValidationError(nameof(RealTeamRequest.Version), "Informe a versão lida.")]);
        }

        return ToResult(await service.UpdateAsync(
            competitionId,
            teamId,
            new RealTeamDefinition(request.Name ?? string.Empty),
            request.Version,
            cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid competitionId,
        Guid teamId,
        IRealTeamService service,
        CancellationToken cancellationToken) =>
        await service.ArchiveAsync(competitionId, teamId, cancellationToken).ConfigureAwait(false) switch
        {
            RealTeamCommandOutcome.Completed => Results.NoContent(),
            _ => Results.NotFound(),
        };

    private static IResult ToResult(RealTeamCommandResult result, Guid? createdAt = null) => result.Outcome switch
    {
        RealTeamCommandOutcome.Completed when createdAt is not null => Results.Created(
            $"/api/v1/competitions/{createdAt}/teams/{result.Team!.Id}", result.Team),
        RealTeamCommandOutcome.Completed => Results.Ok(result.Team),
        RealTeamCommandOutcome.Invalid => DomainRequests.SportsCatalogValidationProblem(result.Errors),
        RealTeamCommandOutcome.NotFound => Results.NotFound(),
        RealTeamCommandOutcome.Duplicate => Results.Problem(
            title: "Nome de time repetido",
            detail: "Já existe um time com esse nome neste campeonato.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = DuplicateNameCode }),
        _ => Results.Problem(
            title: "Time alterado por outra pessoa",
            detail: "Atualize a lista antes de salvar de novo.",
            statusCode: StatusCodes.Status409Conflict),
    };
}

public sealed record RealTeamRequest(string? Name, string? Version);
