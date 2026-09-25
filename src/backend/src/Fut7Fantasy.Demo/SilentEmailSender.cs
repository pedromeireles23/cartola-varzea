using Fut7Fantasy.Application.Abstractions;

namespace Fut7Fantasy.Demo;

/// <summary>
/// O seed não manda e-mail: as contas nascem confirmadas e ninguém recebe convite. Sem
/// isto, uma carga contra a nuvem dispararia mensagens para endereços fictícios.
/// </summary>
internal sealed class SilentEmailSender : IEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
