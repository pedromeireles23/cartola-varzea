using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.SportsCatalog;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Cadastro manual de atletas e inscrições do campeonato.</summary>
public static class AthleteEndpoints
{
    public const string DuplicateNameCode = "athlete_sporting_name_duplicate";
    public const string TransferNotAllowedCode = "athlete_transfer_not_allowed";
    public const string PositionLockedCode = "athlete_position_locked";

    public const string RegistrationClosedCode = "athlete_registration_closed";

    public static IEndpointRouteBuilder MapAthleteEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var athletes = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/athletes")
            .WithTags("Catálogo esportivo");

        athletes.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("ListAthletes")
            .WithSummary("Atletas inscritos no campeonato, incluindo os desligados.");
        athletes.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("CreateAthlete")
            .WithSummary("Cadastra um atleta e o inscreve em um time.");
        athletes.MapPut("/{athleteId:guid}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateAthlete")
            .WithSummary("Altera atleta e preço sem permitir transferência; exige a versão lida.");
        athletes.MapDelete("/{athleteId:guid}", ReleaseAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("ReleaseAthlete")
            .WithSummary("Desliga o atleta, preservando preço e histórico.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid competitionId,
        IAthleteService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(competitionId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(
        Guid competitionId,
        AthleteRequest request,
        IAthleteService service,
        CancellationToken cancellationToken)
    {
        if (ToDefinition(request) is not { } definition)
        {
            return EnumProblem(request);
        }

        var result = await service.CreateAsync(
            competitionId, request.RealTeamId, definition, cancellationToken).ConfigureAwait(false);
        return result.Outcome == AthleteCommandOutcome.Completed
            ? Results.Created($"/api/v1/competitions/{competitionId}/athletes/{result.Athlete!.Id}", result.Athlete)
            : ToResult(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid competitionId,
        Guid athleteId,
        AthleteRequest request,
        IAthleteService service,
        CancellationToken cancellationToken)
    {
        if (ToDefinition(request) is not { } definition)
        {
            return EnumProblem(request);
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return DomainRequests.SportsCatalogValidationProblem(
                [new(nameof(AthleteRequest.Version), "Informe a versão lida.")]);
        }

        return ToResult(await service.UpdateAsync(
            competitionId,
            athleteId,
            request.RealTeamId,
            definition,
            request.Version,
            cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ReleaseAsync(
        Guid competitionId,
        Guid athleteId,
        IAthleteService service,
        CancellationToken cancellationToken) =>
        await service.ReleaseAsync(competitionId, athleteId, cancellationToken).ConfigureAwait(false) switch
        {
            AthleteCommandOutcome.Completed => Results.NoContent(),
            _ => Results.NotFound(),
        };

    private static AthleteDefinition? ToDefinition(AthleteRequest request)
    {
        if (!DomainRequests.TryParseName<Position>(request.Position, out var position)
            || !DomainRequests.TryParseName<PriceTier>(request.PriceTier, out var priceTier))
        {
            return null;
        }

        return new AthleteDefinition(
            request.SportingName ?? string.Empty,
            position,
            priceTier,
            request.InitialPriceOverride);
    }

    private static IResult EnumProblem(AthleteRequest request)
    {
        var errors = new List<SportsCatalogValidationError>();
        if (!DomainRequests.TryParseName<Position>(request.Position, out _))
        {
            errors.Add(new(nameof(AthleteRequest.Position), "Escolha uma posição válida."));
        }

        if (!DomainRequests.TryParseName<PriceTier>(request.PriceTier, out _))
        {
            errors.Add(new(nameof(AthleteRequest.PriceTier), "Escolha um nível de preço válido."));
        }

        return DomainRequests.SportsCatalogValidationProblem(errors);
    }

    private static IResult ToResult(AthleteCommandResult result) => result.Outcome switch
    {
        AthleteCommandOutcome.Completed => Results.Ok(result.Athlete),
        AthleteCommandOutcome.Invalid => DomainRequests.SportsCatalogValidationProblem(result.Errors),
        AthleteCommandOutcome.NotFound => Results.NotFound(),
        AthleteCommandOutcome.TeamUnavailable => Results.Problem(
            title: "Time indisponível",
            detail: "Escolha um time ativo deste campeonato.",
            statusCode: StatusCodes.Status409Conflict),
        AthleteCommandOutcome.Duplicate => Conflict(
            "Nome esportivo repetido",
            "Já existe um atleta com esse nome esportivo neste campeonato.",
            DuplicateNameCode),
        AthleteCommandOutcome.TransferNotAllowed => Conflict(
            "Transferência não permitida",
            "O time de um atleta não pode ser alterado durante o campeonato.",
            TransferNotAllowedCode),
        AthleteCommandOutcome.PositionLocked => Conflict(
            "Posição já utilizada no mercado",
            "A posição não pode mudar depois que o atleta fica disponível no mercado.",
            PositionLockedCode),
        AthleteCommandOutcome.RegistrationClosed => Conflict(
            "Inscrições encerradas",
            "O prazo de inscrição deste campeonato terminou. Para inscrever mais atletas, "
            + "estenda o prazo nas configurações.",
            RegistrationClosedCode),
        _ => Conflict(
            "Atleta alterado por outra pessoa",
            "Atualize a lista antes de salvar de novo.",
            "athlete_concurrency_conflict"),
    };

    private static IResult Conflict(string title, string detail, string code) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record AthleteRequest(
    string? SportingName,
    string? Position,
    Guid RealTeamId,
    string? PriceTier,
    decimal? InitialPriceOverride,
    string? Version);
