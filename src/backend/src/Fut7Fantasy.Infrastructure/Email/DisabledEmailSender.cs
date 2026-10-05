using Fut7Fantasy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.Email;

/// <summary>
/// O envio de uma publicação sem e-mail (<c>Email:Enabled=false</c>). Os fluxos que só
/// existem por e-mail ficam fechados na API; o que sobra — convite de auxiliar, aviso de
/// solicitação — segue sem mensagem, e o log diz por quê, sem destinatário nem assunto.
/// </summary>
public sealed partial class DisabledEmailSender(ILogger<DisabledEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken)
    {
        MessageNotSent(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "E-mail desligado nesta publicação: a mensagem não foi enviada.")]
    private static partial void MessageNotSent(ILogger logger);
}
