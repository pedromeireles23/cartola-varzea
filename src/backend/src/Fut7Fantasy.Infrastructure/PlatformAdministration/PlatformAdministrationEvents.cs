using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.PlatformAdministration;

/// <summary>Eventos da administração da plataforma, sem e-mail nem segredo.</summary>
public static partial class PlatformAdministrationEvents
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "Papel PlatformAdmin concedido pela configuração. userId={UserId}")]
    public static partial void InitialAdminGranted(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Administrador inicial configurado, mas a conta ainda não existe. "
            + "O papel será concedido quando o e-mail for confirmado.")]
    public static partial void InitialAdminAccountMissing(ILogger logger);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Administrador inicial configurado, mas o e-mail da conta não foi confirmado. userId={UserId}")]
    public static partial void InitialAdminEmailNotConfirmed(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Warning,
        Message = "Não foi possível conceder o administrador inicial. A aplicação continua subindo.")]
    public static partial void InitialAdminGrantFailed(ILogger logger, Exception exception);
}
