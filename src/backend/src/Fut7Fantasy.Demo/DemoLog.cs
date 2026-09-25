using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Demo;

/// <summary>Mensagens do reset da demonstração. Nunca levam senha.</summary>
internal static partial class DemoLog
{
    [LoggerMessage(EventId = 9000, Level = LogLevel.Information,
        Message = "Demo recriada: /c/{Slug}, {Entries} equipes fantasy. Visitante: {Viewer}.")]
    public static partial void Ready(ILogger logger, string slug, int entries, string viewer);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Error, Message = "O reset da demo parou: {Reason}")]
    public static partial void Failed(ILogger logger, string reason);
}
