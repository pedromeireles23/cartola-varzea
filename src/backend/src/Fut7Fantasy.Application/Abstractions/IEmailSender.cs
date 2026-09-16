namespace Fut7Fantasy.Application.Abstractions;

/// <summary>
/// Envio de e-mail transacional. No MVP so existem verificacao, recuperacao e
/// alertas de seguranca; convite e resultado usam notificacao interna.
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
