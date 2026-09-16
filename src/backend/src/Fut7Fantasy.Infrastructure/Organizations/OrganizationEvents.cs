using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.Organizations;

/// <summary>Eventos operacionais do módulo sem token nem e-mail completo.</summary>
public static partial class OrganizationEvents
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Error,
        Message = "Falha ao enviar e-mail de convite. invitationId={InvitationId}")]
    public static partial void InvitationEmailFailed(
        ILogger logger,
        Guid invitationId,
        Exception exception);
}
