using System.Globalization;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Pote de cookies do cliente de teste que julga a validade pelo relógio da aplicação.
///
/// O <see cref="System.Net.CookieContainer"/> do cliente padrão descarta o cookie cujo
/// <c>Expires</c> já passou no relógio da máquina. Com a API num relógio parado em
/// <see cref="ApiFactory.FixedNow"/>, a sessão de 14 dias vencia para o cliente em
/// 30/09/2026 — e, a partir de 01/10/2026, toda requisição autenticada desses testes
/// voltava 401, sem nada ter mudado no produto. Aqui o cliente enxerga o mesmo tempo
/// que o servidor: um cookie vale enquanto o relógio controlado não passar do prazo.
/// </summary>
internal sealed class ClockCookieHandler(TimeProvider clock) : DelegatingHandler
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Value, DateTimeOffset? Expires)> _cookies =
        new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = CookieHeader();
        if (header.Length > 0)
        {
            request.Headers.Add("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                Store(value);
            }
        }

        return response;
    }

    private string CookieHeader()
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            return string.Join(
                "; ",
                _cookies
                    .Where(cookie => cookie.Value.Expires is null || cookie.Value.Expires > now)
                    .Select(cookie => $"{cookie.Key}={cookie.Value.Value}"));
        }
    }

    private void Store(string setCookie)
    {
        var parts = setCookie.Split(';');
        var separator = parts[0].IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        var name = parts[0][..separator].Trim();
        var value = parts[0][(separator + 1)..].Trim();
        var now = clock.GetUtcNow();
        DateTimeOffset? expires = null;

        foreach (var attribute in parts.Skip(1).Select(part => part.Trim()))
        {
            if (attribute.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(attribute[8..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                // Max-Age vence o Expires, como manda a RFC 6265.
                expires = now.AddSeconds(seconds);
                break;
            }

            if (attribute.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(
                    attribute[8..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                expires = date;
            }
        }

        lock (_gate)
        {
            // O servidor apaga um cookie mandando-o já vencido.
            if (expires is not null && expires <= now)
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = (value, expires);
            }
        }
    }
}
