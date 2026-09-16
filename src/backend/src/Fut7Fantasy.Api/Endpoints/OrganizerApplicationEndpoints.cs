using System.ComponentModel.DataAnnotations;
using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.PlatformAdministration;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Solicitações de organizador e sua análise administrativa.</summary>
public static class OrganizerApplicationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizerApplicationEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var applications = routes.MapGroup("/api/v1/organizer-applications")
            .WithTags("Organizações")
            .RequireAuthorization();

        applications.MapPost("/", SubmitAsync)
            .WithName("SubmitOrganizerApplication")
            .WithSummary("Solicita a criação de uma organização.");
        applications.MapGet("/mine", GetMineAsync)
            .WithName("GetMyOrganizerApplications")
            .WithSummary("Lista as solicitações da conta atual.");

        var administration = routes.MapGroup("/api/v1/platform-admin/organizer-applications")
            .WithTags("Administração da plataforma")
            .RequireAuthorization(AuthorizationPolicies.PlatformAdmin);

        administration.MapGet("/pending", GetPendingAsync)
            .WithName("GetPendingOrganizerApplications")
            .WithSummary("Lista solicitações aguardando análise.");
        administration.MapPost("/{applicationId:guid}/approve", ApproveAsync)
            .WithName("ApproveOrganizerApplication")
            .WithSummary("Aprova a solicitação e cria a organização.");
        administration.MapPost("/{applicationId:guid}/reject", RejectAsync)
            .WithName("RejectOrganizerApplication")
            .WithSummary("Rejeita a solicitação com motivo auditável.");

        return routes;
    }

    private static async Task<IResult> SubmitAsync(
        OrganizerApplicationRequest request,
        IOrganizerApplicationService service,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var result = await service.SubmitAsync(request.OrganizationName, cancellationToken)
            .ConfigureAwait(false);
        return result.Created
            ? Results.Created($"/api/v1/organizer-applications/{result.Application.Id}", result.Application)
            : Results.Ok(result.Application);
    }

    private static async Task<IResult> GetMineAsync(
        IOrganizerApplicationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetMineAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetPendingAsync(
        IOrganizerApplicationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetPendingAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ApproveAsync(
        Guid applicationId,
        ReviewOrganizerApplicationRequest request,
        IOrganizerApplicationService service,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        return ToResult(await service.ApproveAsync(applicationId, request.Reason, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> RejectAsync(
        Guid applicationId,
        ReviewOrganizerApplicationRequest request,
        IOrganizerApplicationService service,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        return ToResult(await service.RejectAsync(applicationId, request.Reason, cancellationToken)
            .ConfigureAwait(false));
    }

    private static IResult ToResult(ReviewResult result) => result.Outcome switch
    {
        ReviewOutcome.Completed or ReviewOutcome.AlreadyCompleted => Results.Ok(result.Application),
        ReviewOutcome.NotFound => Results.NotFound(),
        _ => Results.Conflict(result.Application),
    };

    private static IResult? Validate<T>(T request)
        where T : notnull
    {
        var errors = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
        {
            return null;
        }

        var byField = errors
            .SelectMany(error => error.MemberNames.DefaultIfEmpty(string.Empty),
                (error, field) => (Field: field, error.ErrorMessage))
            .GroupBy(item => item.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ErrorMessage ?? "Valor inválido.").ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(byField, title: "Dados inválidos");
    }
}

public sealed record OrganizerApplicationRequest(
    [property: Required(ErrorMessage = "Informe o nome da organização.")]
    [property: StringLength(120, MinimumLength = 3)]
    string OrganizationName);

public sealed record ReviewOrganizerApplicationRequest(
    [property: Required(ErrorMessage = "Informe o motivo da decisão.")]
    [property: StringLength(500, MinimumLength = 3)]
    string Reason);
