namespace Fut7Fantasy.Application.Abstractions;

/// <summary>
/// Envio de e-mail transacional para conta, segurança e convites.
/// </summary>
public interface IEmailSender
{
    /// <summary>Envia uma mensagem. Falha de envio nao deve desfazer a operacao de conta.</summary>
    Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken);
}
