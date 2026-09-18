using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Fut7Fantasy.Api.Security;

/// <summary>
/// Prazo absoluto da sessão (04-seguranca §5), que a expiração deslizante do cookie
/// não oferece: a cada renovação o cookie recebe um novo <c>IssuedUtc</c>, então o
/// instante da entrada precisa viajar no próprio ticket.
/// </summary>
public static class AbsoluteSessionExpiry
{
    private const string DeadlineItem = ".fut7fantasy.absolute-expiry";

    /// <summary>
    /// Encadeia os eventos do cookie sem substituir os anteriores; o do Identity é o
    /// que revalida o security stamp e não pode se perder.
    /// </summary>
    public static void Apply(CookieAuthenticationOptions options, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(options);

        var onSigningIn = options.Events.OnSigningIn;
        var onValidatePrincipal = options.Events.OnValidatePrincipal;

        options.Events.OnSigningIn = async context =>
        {
            // Uma reemissão (troca de papel, por exemplo) mantém o prazo da entrada.
            if (!context.Properties.Items.ContainsKey(DeadlineItem))
            {
                var deadline = Now(context.HttpContext).Add(lifetime);
                context.Properties.Items[DeadlineItem] = deadline.ToString("O", CultureInfo.InvariantCulture);
            }

            await onSigningIn(context).ConfigureAwait(false);
        };

        options.Events.OnValidatePrincipal = async context =>
        {
            // Sem prazo registrado a sessão é recusada: falhar fechado custa só uma entrada.
            if (!context.Properties.Items.TryGetValue(DeadlineItem, out var text)
                || !DateTimeOffset.TryParseExact(
                    text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline)
                || Now(context.HttpContext) >= deadline)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(context.Scheme.Name).ConfigureAwait(false);
                return;
            }

            await onValidatePrincipal(context).ConfigureAwait(false);
        };
    }

    private static DateTimeOffset Now(HttpContext context) =>
        context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
}
