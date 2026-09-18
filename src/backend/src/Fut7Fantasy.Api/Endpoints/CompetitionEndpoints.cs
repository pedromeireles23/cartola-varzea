using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Perfis de modalidade e campeonatos na área de organização.</summary>
public static class CompetitionEndpoints
{
    /// <summary>Código estável de quem tenta trocar a modalidade de um campeonato publicado.</summary>
    public const string ModalityLockedCode = "competition_modality_locked";

    /// <summary>Código estável de quem tenta publicar com impedimentos no checklist.</summary>
    public const string NotReadyCode = "competition_not_ready";

    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Regras públicas: são as mesmas que a página de regras vai mostrar.
        routes.MapGet("/api/v1/modality-profiles", () =>
                Results.Ok(ModalityProfiles.Current.Select(ModalityProfileView.From)))
            .WithTags("Campeonatos")
            .WithName("GetModalityProfiles")
            .WithSummary("Formação, banco, orçamento e limites vigentes de cada modalidade.");

        var organizationCompetitions = routes
            .MapGroup("/api/v1/organizations/{organizationId:guid}/competitions")
            .WithTags("Campeonatos");

        organizationCompetitions.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationMember)
            .WithName("ListOrganizationCompetitions")
            .WithSummary("Campeonatos da organização, inclusive rascunhos.");
        organizationCompetitions.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationOwnerWrite)
            .WithName("CreateCompetitionDraft")
            .WithSummary("Cria um campeonato em rascunho.");

        var competition = routes.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .WithTags("Campeonatos");

        competition.MapGet("/settings", GetSettingsAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("GetCompetitionSettings")
            .WithSummary("Configuração do campeonato para a equipe da organização.");
        competition.MapPut("/settings", UpdateSettingsAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("UpdateCompetitionSettings")
            .WithSummary("Substitui a configuração; exige a versão lida para detectar edição concorrente.");

        competition.MapGet("/readiness", GetReadinessAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("GetCompetitionReadiness")
            .WithSummary("Checklist de prontidão para publicar, com impedimentos e alertas.");
        competition.MapPut("/publication", SetPublicationAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("SetCompetitionPublication")
            .WithSummary("Publica ou volta o campeonato para rascunho; exige a versão lida.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        ICompetitionManagementService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(organizationId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        CompetitionSettingsRequest request,
        ICompetitionManagementService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Na criação, o que é opcional assume o padrão do produto (01 §9).
        var errors = new List<CompetitionSettingsError>();
        var modality = ParseModality(request.Modality, errors);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        var settings = new CompetitionSettings(
            request.Name ?? string.Empty,
            request.Season ?? string.Empty,
            modality,
            request.TimeZoneId ?? CompetitionSettings.DefaultTimeZoneId,
            TimeSpan.FromMinutes(request.MarketCloseLeadTimeMinutes ?? 0),
            request.ResultsSlaBusinessDays ?? CompetitionSettings.DefaultResultsSlaBusinessDays,
            request.CorrectionWindowBusinessDays ?? CompetitionSettings.DefaultCorrectionWindowBusinessDays,
            request.RegistrationDeadlineLocal);

        var result = await service.CreateDraftAsync(organizationId, settings, cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome == CompetitionCommandOutcome.Completed
            ? Results.Created($"/api/v1/competitions/{result.Competition!.Id}/settings", result.Competition)
            : ToResult(result);
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid competitionId,
        ICompetitionManagementService service,
        CancellationToken cancellationToken) =>
        await service.GetAsync(competitionId, cancellationToken).ConfigureAwait(false) is { } competition
            ? Results.Ok(competition)
            : Results.NotFound();

    private static async Task<IResult> UpdateSettingsAsync(
        Guid competitionId,
        UpdateCompetitionSettingsRequest request,
        ICompetitionManagementService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A edição substitui tudo: um campo ausente seria reescrito com o padrão sem aviso.
        var errors = new List<CompetitionSettingsError>();
        var modality = ParseModality(request.Modality, errors);
        Require(request.TimeZoneId, nameof(request.TimeZoneId), errors);
        Require(request.MarketCloseLeadTimeMinutes, nameof(request.MarketCloseLeadTimeMinutes), errors);
        Require(request.ResultsSlaBusinessDays, nameof(request.ResultsSlaBusinessDays), errors);
        Require(request.CorrectionWindowBusinessDays, nameof(request.CorrectionWindowBusinessDays), errors);
        Require(request.Version, nameof(request.Version), errors);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        var settings = new CompetitionSettings(
            request.Name ?? string.Empty,
            request.Season ?? string.Empty,
            modality,
            request.TimeZoneId!,
            TimeSpan.FromMinutes(request.MarketCloseLeadTimeMinutes!.Value),
            request.ResultsSlaBusinessDays!.Value,
            request.CorrectionWindowBusinessDays!.Value,
            request.RegistrationDeadlineLocal);

        return ToResult(await service.UpdateSettingsAsync(
            competitionId, settings, request.Version!, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> GetReadinessAsync(
        Guid competitionId,
        ICompetitionPublicationService service,
        CancellationToken cancellationToken) =>
        await service.GetReadinessAsync(competitionId, cancellationToken).ConfigureAwait(false) is { } readiness
            ? Results.Ok(readiness)
            : Results.NotFound();

    private static async Task<IResult> SetPublicationAsync(
        Guid competitionId,
        CompetitionPublicationRequest request,
        ICompetitionPublicationService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<CompetitionSettingsError>();
        Require(request.Published, nameof(request.Published), errors);
        Require(request.Version, nameof(request.Version), errors);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        var result = await service.SetPublishedAsync(
            competitionId, request.Published!.Value, request.Version!, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            CompetitionPublicationOutcome.Completed => Results.Ok(result.Readiness),
            CompetitionPublicationOutcome.NotFound => Results.NotFound(),
            CompetitionPublicationOutcome.NotReady => Results.Problem(
                title: "Campeonato ainda não pode ser publicado",
                detail: "Resolva os impedimentos do checklist antes de publicar.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = NotReadyCode,
                    ["readiness"] = result.Readiness,
                }),
            _ => Results.Problem(
                title: "Campeonato alterado por outra pessoa",
                detail: "Atualize a página para ver a versão atual antes de publicar.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static IResult ToResult(CompetitionCommandResult result) => result.Outcome switch
    {
        CompetitionCommandOutcome.Completed => Results.Ok(result.Competition),
        CompetitionCommandOutcome.Invalid => ValidationProblem(result.Errors),
        CompetitionCommandOutcome.NotFound => Results.NotFound(),
        CompetitionCommandOutcome.ModalityLocked => Results.Problem(
            title: "Modalidade travada",
            detail: "A modalidade não muda depois da publicação.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = ModalityLockedCode }),
        _ => Results.Problem(
            title: "Configuração alterada por outra pessoa",
            detail: "Atualize a página para ver a versão atual antes de salvar.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static Modality ParseModality(string? value, List<CompetitionSettingsError> errors)
    {
        if (DomainRequests.TryParseName<Modality>(value, out var modality))
        {
            return modality;
        }

        errors.Add(new(nameof(CompetitionSettings.Modality), "Escolha Fut7, futsal ou campo."));
        return default;
    }

    private static void Require<T>(T? value, string field, List<CompetitionSettingsError> errors)
    {
        if (value is null)
        {
            errors.Add(new(field, "Informe este campo."));
        }
    }

    private static IResult ValidationProblem(IEnumerable<CompetitionSettingsError> errors) =>
        DomainRequests.ValidationProblem(
            errors,
            // O domínio fala em TimeSpan; a API recebe minutos.
            field => field == nameof(CompetitionSettings.MarketCloseLeadTime)
                ? nameof(CompetitionSettingsRequest.MarketCloseLeadTimeMinutes)
                : field);
}

public sealed record CompetitionSettingsRequest(
    string? Name,
    string? Season,
    string? Modality,
    string? TimeZoneId,
    int? MarketCloseLeadTimeMinutes,
    int? ResultsSlaBusinessDays,
    int? CorrectionWindowBusinessDays,
    string? RegistrationDeadlineLocal = null);

/// <summary>Decisão de publicação, com a versão devolvida pela leitura.</summary>
public sealed record CompetitionPublicationRequest(bool? Published, string? Version);

/// <summary>Configuração completa, com a versão devolvida pela leitura.</summary>
public sealed record UpdateCompetitionSettingsRequest(
    string? Name,
    string? Season,
    string? Modality,
    string? TimeZoneId,
    int? MarketCloseLeadTimeMinutes,
    int? ResultsSlaBusinessDays,
    int? CorrectionWindowBusinessDays,
    string? Version,
    string? RegistrationDeadlineLocal = null);
