using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Perfis de modalidade e campeonatos na área de organização.</summary>
public static class CompetitionEndpoints
{
    /// <summary>Código estável de quem tenta trocar a modalidade de um campeonato publicado.</summary>
    public const string ModalityLockedCode = "competition_modality_locked";

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
            request.CorrectionWindowBusinessDays ?? CompetitionSettings.DefaultCorrectionWindowBusinessDays);

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
            request.CorrectionWindowBusinessDays!.Value);

        return ToResult(await service.UpdateSettingsAsync(
            competitionId, settings, request.Version!, cancellationToken).ConfigureAwait(false));
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

    /// <summary>
    /// Só aceita o nome da modalidade. Números passariam no <see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/>
    /// e criariam uma dependência do valor interno da enumeração.
    /// </summary>
    private static Modality ParseModality(string? value, List<CompetitionSettingsError> errors)
    {
        if (value is not null
            && Enum.GetNames<Modality>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse<Modality>(value, out var modality))
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
        Results.ValidationProblem(
            errors
                .GroupBy(
                    // O domínio fala em TimeSpan; a API recebe minutos.
                    error => error.Field == nameof(CompetitionSettings.MarketCloseLeadTime)
                        ? nameof(CompetitionSettingsRequest.MarketCloseLeadTimeMinutes)
                        : error.Field,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal),
            title: "Dados inválidos");
}

public sealed record CompetitionSettingsRequest(
    string? Name,
    string? Season,
    string? Modality,
    string? TimeZoneId,
    int? MarketCloseLeadTimeMinutes,
    int? ResultsSlaBusinessDays,
    int? CorrectionWindowBusinessDays);

/// <summary>Configuração completa, com a versão devolvida pela leitura.</summary>
public sealed record UpdateCompetitionSettingsRequest(
    string? Name,
    string? Season,
    string? Modality,
    string? TimeZoneId,
    int? MarketCloseLeadTimeMinutes,
    int? ResultsSlaBusinessDays,
    int? CorrectionWindowBusinessDays,
    string? Version);
