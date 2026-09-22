using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Notifications;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>
/// A caixa de avisos da conta da sessão (01 §14). Não existe rota para ler ou mexer na
/// caixa de outra pessoa: o serviço usa sempre a conta autenticada.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var notifications = routes.MapGroup("/api/v1/notifications").WithTags("Avisos");

        notifications.MapGet("/", InboxAsync)
            .RequireAuthorization()
            .WithName("GetNotifications")
            .WithSummary("Avisos da conta, do mais recente para o mais antigo, com o total por ler.");
        notifications.MapPost("/read", MarkAllReadAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedWrite)
            .WithName("MarkNotificationsRead")
            .WithSummary("Marca todos os avisos da conta como lidos e devolve a caixa atualizada.");

        return routes;
    }

    private static async Task<IResult> InboxAsync(
        INotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.InboxAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> MarkAllReadAsync(
        INotificationService service,
        CancellationToken cancellationToken)
    {
        await service.MarkAllReadAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(await service.InboxAsync(cancellationToken).ConfigureAwait(false));
    }
}
