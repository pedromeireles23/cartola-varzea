using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Administrador inicial concedido pela configuração.</summary>
public sealed partial class InitialPlatformAdminTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string SenhaValida = "uma-senha-bem-longa-2026";
    private const string Setting = "PlatformAdministration:InitialAdminEmail";
    private const string GrantAction = "PlatformAdminGrantedByConfiguration";

    [GeneratedRegex(@"https://testes\.local/verificar-email\?id=(?<id>[0-9a-f-]+)&token=(?<token>[^\s]+)")]
    private static partial Regex VerificationLink { get; }

    [Fact]
    public async Task ConfirmingConfiguredEmailGrantsPlatformAdminOnlyToThatAccount()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var emails = new CapturingEmailSender();
        var adminEmail = UniqueEmail("admin");
        var otherEmail = UniqueEmail("comum");
        using var baseFactory = sqlServer.CreateApi(emails);
        using var factory = WithInitialAdmin(baseFactory, adminEmail);

        var adminId = await RegisterAndConfirmAsync(factory, emails, adminEmail, cancellationToken);
        await RegisterAndConfirmAsync(factory, emails, otherEmail, cancellationToken);

        using var admin = await LoginAsync(factory, adminEmail, cancellationToken);
        using var queue = await admin.GetAsync(
            new Uri("/api/v1/platform-admin/organizer-applications/pending", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        var profile = await admin.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Contains(
            profile.GetProperty("roles").EnumerateArray(),
            role => role.GetString() == ApplicationRole.PlatformAdmin);

        using var other = await LoginAsync(factory, otherEmail, cancellationToken);
        using var denied = await other.GetAsync(
            new Uri("/api/v1/platform-admin/organizer-applications/pending", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        Assert.Equal(1, await CountGrantAuditAsync(factory, adminId, cancellationToken));
    }

    [Fact]
    public async Task StartupGrantsExistingConfirmedAccountOnce()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("existente");
        using var withoutSetting = sqlServer.CreateApi();
        var userId = await CreateUserAsync(withoutSetting, email, emailConfirmed: true);
        Assert.False(await IsPlatformAdminAsync(withoutSetting, userId));

        using (var firstBase = sqlServer.CreateApi())
        using (var firstStart = WithInitialAdmin(firstBase, email))
        {
            // Criar o cliente sobe o host, e com ele os serviços de inicialização.
            using var client = firstStart.CreateClient();
            Assert.True(await IsPlatformAdminAsync(firstStart, userId));
        }

        using (var secondBase = sqlServer.CreateApi())
        using (var secondStart = WithInitialAdmin(secondBase, email))
        {
            using var client = secondStart.CreateClient();
            Assert.Equal(1, await CountGrantAuditAsync(secondStart, userId, cancellationToken));
        }
    }

    [Fact]
    public async Task StartupDoesNotGrantUnconfirmedAccount()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var email = UniqueEmail("nao-confirmada");
        using var withoutSetting = sqlServer.CreateApi();
        var userId = await CreateUserAsync(withoutSetting, email, emailConfirmed: false);

        using var baseFactory = sqlServer.CreateApi();
        using var factory = WithInitialAdmin(baseFactory, email);
        using var client = factory.CreateClient();

        Assert.False(await IsPlatformAdminAsync(factory, userId));
    }

    [Fact]
    public void ApplicationRefusesToStartWithInvalidInitialAdminEmail()
    {
        var exception = ApiFactory.RefusesToStart(() =>
            WithInitialAdmin(new ApiFactory(), "isto-nao-e-email"));

        Assert.Contains("InitialAdminEmail", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> WithInitialAdmin(
        WebApplicationFactory<Program> factory,
        string email) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(Setting, email));

    private static async Task<Guid> RegisterAndConfirmAsync(
        WebApplicationFactory<Program> factory,
        CapturingEmailSender emails,
        string email,
        CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(factory, cancellationToken);
        using var registration = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, displayName = "Pessoa de Teste", password = SenhaValida },
            cancellationToken);
        registration.EnsureSuccessStatusCode();

        var match = VerificationLink.Match(emails.LastTo(email).TextBody);
        Assert.True(match.Success, "Nenhum link de verificação encontrado.");
        var userId = Guid.Parse(match.Groups["id"].Value);
        using var confirmation = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId, token = HttpUtility.UrlDecode(match.Groups["token"].Value) },
            cancellationToken);
        confirmation.EnsureSuccessStatusCode();
        return userId;
    }

    private static async Task<Guid> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            DisplayName = "Pessoa de Teste",
            CreatedAt = ApiFactory.FixedNow,
        };
        Assert.True((await users.CreateAsync(user, SenhaValida)).Succeeded);
        return user.Id;
    }

    private static async Task<bool> IsPlatformAdminAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await users.IsInRoleAsync(user, ApplicationRole.PlatformAdmin);
    }

    private static async Task<int> CountGrantAuditAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        return await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.TargetId == userId && entry.Action == GrantAction,
            cancellationToken);
    }

    private static async Task<HttpClient> LoginAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(factory, cancellationToken);
        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = SenhaValida },
            cancellationToken);
        login.EnsureSuccessStatusCode();
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    private static async Task<HttpClient> CreateClientAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    private static async Task RefreshAntiforgeryAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri("/api/v1/auth/antiforgery", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var requestToken = response.Headers.GetValues("Set-Cookie")
            .Select(cookie => Regex.Match(cookie, @"XSRF-TOKEN=(?<value>[^;]+)"))
            .First(match => match.Success)
            .Groups["value"].Value;
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", HttpUtility.UrlDecode(requestToken));
    }

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";
}
