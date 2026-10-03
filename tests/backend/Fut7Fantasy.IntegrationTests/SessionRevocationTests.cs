using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Sessão guardada no servidor (04-seguranca §5): o logout revoga aquela sessão, e um
/// cookie reemitido por uma resposta que estava em voo não a traz de volta.
/// </summary>
public sealed class SessionRevocationTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string SessionCookie = "fut7fantasy.session";

    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);

    private static readonly Uri Logout = new("/api/v1/auth/logout", UriKind.Relative);

    [Fact]
    public async Task CookieDeUmaRespostaEmVooNaoReabreASessaoDepoisDoLogout()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = TestAccounts.UniqueEmail("logout-em-voo");
        await TestAccounts.CreateUserAsync(factory, email);
        using var client = await TestAccounts.CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        // Toda resposta autenticada reemite o cookie de sessão. Este é o cookie que uma
        // requisição disparada antes do logout entregaria ao navegador depois dele.
        using var emVoo = await client.GetAsync(Me, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, emVoo.StatusCode);
        var cookieAtrasado = SessionCookieOf(emVoo);

        using var saida = await client.PostAsync(Logout, null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, saida.StatusCode);

        using var depois = await SendWithCookieAsync(factory, cookieAtrasado, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, depois.StatusCode);
    }

    [Fact]
    public async Task LogoutEncerraSoASessaoDeQuemSaiu()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = TestAccounts.UniqueEmail("duas-sessoes");
        await TestAccounts.CreateUserAsync(factory, email);

        // Os visitantes da demonstração dividem a mesma conta: sair num navegador não
        // pode derrubar os outros.
        using var primeiro = await TestAccounts.CreateAuthenticatedClientAsync(factory, email, cancellationToken);
        using var segundo = await TestAccounts.CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        using var saida = await primeiro.PostAsync(Logout, null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, saida.StatusCode);

        using var doPrimeiro = await primeiro.GetAsync(Me, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, doPrimeiro.StatusCode);

        using var doSegundo = await segundo.GetAsync(Me, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, doSegundo.StatusCode);
    }

    [Fact]
    public async Task RenovarDepoisDeRemoverNaoRecriaASessao()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var userId = await TestAccounts.CreateUserAsync(factory, TestAccounts.UniqueEmail("renovar"));
        var store = SessionStoreOf(factory);
        var ticket = Ticket(userId, factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(1));

        var key = await store.StoreAsync(ticket, cancellationToken);
        Assert.NotNull(await store.RetrieveAsync(key, cancellationToken));

        // A ordem de uma resposta em voo: o logout remove, e a renovação chega depois.
        await store.RemoveAsync(key, cancellationToken);
        await store.RenewAsync(key, ticket, cancellationToken);

        Assert.Null(await store.RetrieveAsync(key, cancellationToken));
    }

    [Fact]
    public async Task SessaoVencidaNaoEDevolvida()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(
            new CapturingEmailSender(),
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        var userId = await TestAccounts.CreateUserAsync(factory, TestAccounts.UniqueEmail("vencida"));
        var store = SessionStoreOf(factory);
        var key = await store.StoreAsync(Ticket(userId, clock.GetUtcNow().AddHours(1)), cancellationToken);

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await store.RetrieveAsync(key, cancellationToken));
    }

    private static ITicketStore SessionStoreOf(WebApplicationFactory<Program> factory)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        return options.SessionStore
            ?? throw new InvalidOperationException("O cookie de sessão não usa o armazenamento no servidor.");
    }

    private static AuthenticationTicket Ticket(Guid userId, DateTimeOffset expiresUtc)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            IdentityConstants.ApplicationScheme);
        var properties = new AuthenticationProperties { ExpiresUtc = expiresUtc };
        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, IdentityConstants.ApplicationScheme);
    }

    private static string SessionCookieOf(HttpResponseMessage response)
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';')[0])
                .Where(pair => pair.StartsWith(SessionCookie, StringComparison.Ordinal))
                .ToList()
            : [];
        Assert.NotEmpty(cookies);
        return string.Join("; ", cookies);
    }

    private static async Task<HttpResponseMessage> SendWithCookieAsync(
        WebApplicationFactory<Program> factory,
        string cookie,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Me);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, cancellationToken);
    }
}
