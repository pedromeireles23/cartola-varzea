using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Dois fatores por aplicativo autenticador, obrigatórios para a administração da plataforma
/// (04 §5, Fase 20): o cadastro pelo próprio admin, a entrada em dois passos, os códigos de
/// recuperação e o bloqueio por código errado.
/// </summary>
public sealed class TwoFactorFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly Uri Queue =
        new("/api/v1/platform-admin/organizer-applications/pending", UriKind.Relative);

    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);

    [Fact]
    public async Task AdminEnrollsTheAuthenticatorAndTheSameSessionOpensTheAdministration()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("admin-cadastro");
        await CreateUserAsync(factory, email, ApplicationRole.PlatformAdmin);
        using var admin = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);
        using var otherBrowser = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        // Só a senha não abre a administração; para o admin, a resposta diz que é proibido.
        using (var refused = await admin.GetAsync(Queue, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        var before = await admin.GetFromJsonAsync<JsonElement>(Me, cancellationToken);
        Assert.False(before.GetProperty("twoFactorEnabled").GetBoolean());

        using var setup = await admin.PostAsync(
            new Uri("/api/v1/auth/two-factor/authenticator", UriKind.Relative), null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var sharedKey = body.GetProperty("sharedKey").GetString()!;
        var uri = body.GetProperty("authenticatorUri").GetString()!;
        var key = sharedKey.Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Matches("^[A-Z2-7]{32}$", key);
        Assert.StartsWith("otpauth://totp/Cartola%20V%C3%A1rzea:", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={key}&issuer=Cartola%20V%C3%A1rzea", uri, StringComparison.Ordinal);

        // Gerar a chave troca o security stamp, e a sessão que pediu continua de pé.
        using (var stillSignedIn = await admin.GetAsync(Me, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        }

        using (var wrong = await admin.PostAsJsonAsync(
            new Uri("/api/v1/auth/two-factor/enable", UriKind.Relative),
            new { code = WrongCode(key) },
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using var enabled = await admin.PostAsJsonAsync(
            new Uri("/api/v1/auth/two-factor/enable", UriKind.Relative),
            new { code = Totp.Code(key) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());

        // A sessão que ligou passou a ser de dois fatores, e continua sendo nas próximas
        // requisições — a revalidação do security stamp não pode apagar a marca.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var queue = await admin.GetAsync(Queue, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        }

        var status = await admin.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/auth/two-factor", UriKind.Relative), cancellationToken);
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());

        // Outra sessão aberta só com senha acaba quando os dois fatores ligam.
        using (var old = await otherBrowser.GetAsync(Me, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, old.StatusCode);
        }

        // Com tudo ligado, gerar outra chave desfaria o aplicativo de quem já usa.
        using var again = await admin.PostAsync(
            new Uri("/api/v1/auth/two-factor/authenticator", UriKind.Relative), null, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task WithTwoFactorThePasswordAloneDoesNotOpenTheSession()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("admin-entrada");
        var key = await CreateAdminAsync(factory, email);
        using var client = await NewClientAsync(factory, cancellationToken);

        using var login = await LoginAsync(client, email, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("twoFactorRequired").GetBoolean());

        using (var anonymous = await client.GetAsync(Me, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, anonymous.StatusCode);
        }

        using (var wrong = await SecondFactorAsync(client, WrongCode(key), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (var right = await SecondFactorAsync(client, Totp.Code(key), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        }

        await RefreshAntiforgeryAsync(client, cancellationToken);
        var profile = await client.GetFromJsonAsync<JsonElement>(Me, cancellationToken);
        Assert.True(profile.GetProperty("twoFactorEnabled").GetBoolean());
        using var queue = await client.GetAsync(Queue, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
    }

    [Fact]
    public async Task TheSecondStepNeedsTheFirstOne()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var key = await CreateAdminAsync(factory, UniqueEmail("admin-sem-senha"));
        using var client = await NewClientAsync(factory, cancellationToken);

        using var attempt = await SecondFactorAsync(client, Totp.Code(key), cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
    }

    [Fact]
    public async Task ARecoveryCodeOpensTheSessionOnlyOnce()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("admin-recuperacao");
        await CreateAdminAsync(factory, email);
        string recoveryCode;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            recoveryCode = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user!, 10))!.First();
        }

        using var first = await NewClientAsync(factory, cancellationToken);
        using (var login = await LoginAsync(first, email, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // Digitado em minúsculas, sem o hífen e com espaço, como alguém copia de um papel.
        var typed = $" {recoveryCode.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant()} ";
        using (var used = await SecondFactorAsync(first, typed, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, used.StatusCode);
        }

        using var second = await NewClientAsync(factory, cancellationToken);
        using (var login = await LoginAsync(second, email, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using var reused = await SecondFactorAsync(second, recoveryCode, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task WrongCodesLockTheAccountLikeWrongPasswords()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("admin-bloqueio");
        var key = await CreateAdminAsync(factory, email);
        using var client = await NewClientAsync(factory, cancellationToken);
        using (var login = await LoginAsync(client, email, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await SecondFactorAsync(client, WrongCode(key), cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // Bloqueada, nem o código certo entra, e a resposta é a mesma do código errado.
        using var blocked = await SecondFactorAsync(client, Totp.Code(key), cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
    }

    private static async Task<HttpClient> NewClientAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateDefaultClient(
            new ClockCookieHandler(factory.Services.GetRequiredService<TimeProvider>()));
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = SenhaValida },
            cancellationToken);

    private static Task<HttpResponseMessage> SecondFactorAsync(
        HttpClient client,
        string code,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login/two-factor", UriKind.Relative),
            new { code },
            cancellationToken);

    /// <summary>
    /// Um código de seis dígitos que nenhum passo da janela aceita: o Identity confere dois
    /// passos de 30 segundos para cada lado.
    /// </summary>
    private static string WrongCode(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var accepted = Enumerable.Range(-3, 7)
            .Select(step => Totp.Code(key, now.AddSeconds(step * 30)))
            .ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(0, 1_000_000)
            .Select(value => value.ToString("D6", CultureInfo.InvariantCulture))
            .First(code => !accepted.Contains(code));
    }
}
