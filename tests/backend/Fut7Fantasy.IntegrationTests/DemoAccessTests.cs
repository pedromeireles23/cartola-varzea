using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// "Entrar como visitante" (Fase 12, decisão do Pedro em 2026-09-25): a demonstração
/// pública abre a conta <c>DemoViewer</c> sem senha. O que se prova é o limite — só
/// quando ligada, só para a conta de demonstração, e o que se ganha é leitura.
/// </summary>
public sealed class DemoAccessTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task VisitorEntryDoesNotExistUnlessTheDemoTurnsItOn()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        using var client = await ClientAsync(factory, cancellationToken);

        var disponivel = await client.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/auth/demo", UriKind.Relative), cancellationToken);
        Assert.False(disponivel.GetProperty("available").GetBoolean());

        using var entrada = await client.PostAsync(
            new System.Uri("/api/v1/auth/demo", UriKind.Relative), null, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, entrada.StatusCode);
        await AssertAnonymousAsync(client, cancellationToken);
    }

    [Fact]
    public async Task VisitorEntryOpensAReadOnlySessionThatEndsWithTheBrowser()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("visitante");
        using var factory = DemoApi(email);
        await CreateUserAsync(factory, email, ApplicationRole.DemoViewer, displayName: "Visitante");
        using var client = await ClientAsync(factory, cancellationToken);

        var disponivel = await client.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/auth/demo", UriKind.Relative), cancellationToken);
        Assert.True(disponivel.GetProperty("available").GetBoolean());

        using var entrada = await client.PostAsync(
            new System.Uri("/api/v1/auth/demo", UriKind.Relative), null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, entrada.StatusCode);

        // Cookie de sessão, sem validade: fecha junto com o navegador.
        var sessao = entrada.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(
            "fut7fantasy.session=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", sessao, StringComparison.OrdinalIgnoreCase);

        await RefreshAntiforgeryAsync(client, cancellationToken);
        var conta = await client.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Contains(
            conta.GetProperty("roles").EnumerateArray(),
            role => role.GetString() == ApplicationRole.DemoViewer);

        // E o que ela abre só lê.
        using var escrita = await client.PostAsJsonAsync(
            new System.Uri("/api/v1/organizer-applications", UriKind.Relative),
            new { organizationName = "Liga que não nasce" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
    }

    [Fact]
    public async Task MisconfiguredVisitorEntryNeverOpensAnotherAccount()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("admin-por-engano");
        using var factory = DemoApi(email);
        await CreateUserAsync(factory, email, ApplicationRole.PlatformAdmin, displayName: "Administração");
        using var client = await ClientAsync(factory, cancellationToken);

        using var entrada = await client.PostAsync(
            new System.Uri("/api/v1/auth/demo", UriKind.Relative), null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, entrada.StatusCode);
        await AssertAnonymousAsync(client, cancellationToken);
    }

    private WebApplicationFactory<Program> DemoApi(string viewerEmail) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiFactory.ApplyRequiredSettings(builder, sqlServer.ConnectionString);
            builder.UseSetting("DemoAccess:Enabled", "true");
            builder.UseSetting("DemoAccess:ViewerEmail", viewerEmail);
        });

    private static async Task<HttpClient> ClientAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }

    private static async Task AssertAnonymousAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var me = await client.GetAsync(
            new System.Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, me.StatusCode);
    }
}
