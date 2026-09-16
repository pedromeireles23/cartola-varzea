using System.Net;

namespace Fut7Fantasy.Infrastructure.Organizations;

internal static class OrganizationInvitationEmails
{
    public static (string Subject, string Html, string Text) AssistantInvitation(
        string organizationName,
        Uri link,
        DateTimeOffset expiresAt)
    {
        var safeName = WebUtility.HtmlEncode(organizationName);
        var safeLink = WebUtility.HtmlEncode(link.AbsoluteUri);
        var expiration = expiresAt.ToString("dd/MM/yyyy 'às' HH:mm 'UTC'", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        return (
            $"Convite para auxiliar {organizationName}",
            $"<p>Você recebeu um convite para auxiliar <strong>{safeName}</strong>.</p>"
                + $"<p><a href=\"{safeLink}\">Aceitar convite</a></p>"
                + $"<p>O convite expira em {WebUtility.HtmlEncode(expiration)}.</p>",
            $"Você recebeu um convite para auxiliar {organizationName}.\n\n"
                + $"Aceite em: {link.AbsoluteUri}\n\nO convite expira em {expiration}.");
    }
}
