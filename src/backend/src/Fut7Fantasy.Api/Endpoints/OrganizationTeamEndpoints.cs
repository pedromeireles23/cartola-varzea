using System.ComponentModel.DataAnnotations;
using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Organizations;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Organizações da conta, equipe auxiliar e convites.</summary>
public static class OrganizationTeamEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationTeamEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/v1/organizations/mine", GetMineAsync)
            .RequireAuthorization()
            .WithTags("Organizações")
            .WithName("GetMyOrganizations")
            .WithSummary("Lista as organizações da conta atual e o papel em cada uma.");

        routes.MapGet("/api/v1/organizations/{organizationId:guid}", GetOrganizationAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationMember)
            .WithTags("Organizações")
            .WithName("GetOrganization")
            .WithSummary("Dados da organização e o papel da conta atual nela.");

        var team = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/team")
            .WithTags("Equipe da organização");

        team.MapGet("/", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationOwner)
            .WithName("GetOrganizationTeam")
            .WithSummary("Lista auxiliares e convites da organização.");
        team.MapPost("/invitations", InviteAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationOwnerWrite)
            .WithName("InviteOrganizationAssistant")
            .WithSummary("Convida uma conta para auxiliar a organização.");
        team.MapDelete("/assistants/{userId:guid}", RemoveAssistantAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationOwnerWrite)
            .WithName("RemoveOrganizationAssistant")
            .WithSummary("Retira um auxiliar; o acesso acaba na requisição seguinte.");
        team.MapDelete("/invitations/{invitationId:guid}", RevokeAsync)
            .RequireAuthorization(AuthorizationPolicies.OrganizationOwnerWrite)
            .WithName("RevokeOrganizationInvitation")
            .WithSummary("Revoga um convite ainda pendente.");

        routes.MapPost("/api/v1/organization-invitations/accept", AcceptAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithTags("Equipe da organização")
            .WithName("AcceptOrganizationInvitation")
            .WithSummary("Aceita um convite usando a conta do mesmo e-mail.");

        return routes;
    }

    private static async Task<IResult> GetMineAsync(
        IOrganizationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetMineAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetOrganizationAsync(
        Guid organizationId,
        IOrganizationService service,
        CancellationToken cancellationToken) =>
        await service.GetAsync(organizationId, cancellationToken).ConfigureAwait(false) is { } organization
            ? Results.Ok(organization)
            : Results.NotFound();

    private static async Task<IResult> RemoveAssistantAsync(
        Guid organizationId,
        Guid userId,
        IOrganizationTeamService service,
        CancellationToken cancellationToken) =>
        await service.RemoveAssistantAsync(organizationId, userId, cancellationToken).ConfigureAwait(false) switch
        {
            MemberRemovalOutcome.Removed => Results.NoContent(),
            MemberRemovalOutcome.NotAnAssistant => Results.Conflict(),
            _ => Results.NotFound(),
        };

    private static async Task<IResult> GetAsync(
        Guid organizationId,
        IOrganizationTeamService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetAsync(organizationId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> InviteAsync(
        Guid organizationId,
        InviteOrganizationAssistantRequest request,
        IOrganizationTeamService service,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var invitation = await service.InviteAssistantAsync(
            organizationId, request.Email, cancellationToken).ConfigureAwait(false);
        return Results.Created(
            $"/api/v1/organizations/{organizationId}/team/invitations/{invitation.Id}",
            invitation);
    }

    private static async Task<IResult> AcceptAsync(
        AcceptOrganizationInvitationRequest request,
        IOrganizationTeamService service,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        return ToResult(await service.AcceptInvitationAsync(request.Token, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        IOrganizationTeamService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.RevokeInvitationAsync(
            organizationId, invitationId, cancellationToken).ConfigureAwait(false));

    private static IResult ToResult(InvitationActionResult result) => result.Outcome switch
    {
        InvitationActionOutcome.Completed or InvitationActionOutcome.AlreadyCompleted =>
            Results.Ok(result.Invitation),
        InvitationActionOutcome.NotFound => Results.NotFound(),
        InvitationActionOutcome.Expired => Results.Problem(
            title: "Convite expirado", statusCode: StatusCodes.Status410Gone),
        InvitationActionOutcome.EmailMismatch => Results.Problem(
            title: "Convite destinado a outra conta", statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Conflict(),
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

public sealed record InviteOrganizationAssistantRequest(
    [property: Required(ErrorMessage = "Informe o e-mail.")]
    [property: EmailAddress(ErrorMessage = "E-mail inválido.")]
    [property: StringLength(254)]
    string Email);

public sealed record AcceptOrganizationInvitationRequest(
    [property: Required(ErrorMessage = "Informe o código do convite.")]
    [property: StringLength(2048)]
    string Token);
