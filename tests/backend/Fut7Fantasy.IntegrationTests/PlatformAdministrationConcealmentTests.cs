using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// A administração da plataforma não existe para quem não é Platform admin (Fase 20): cada
/// rota dela responde exatamente como um caminho que a API não tem, com e sem sessão.
/// </summary>
public sealed class PlatformAdministrationConcealmentTests(SqlServerFixture sqlServer)
    : IClassFixture<SqlServerFixture>
{
    private const string Queue = "/api/v1/platform-admin/organizer-applications/pending";
    private const string UnknownQueue = "/api/v1/nao-existe/organizer-applications/pending";

    private static readonly string Approve =
        $"/api/v1/platform-admin/organizer-applications/{Guid.CreateVersion7()}/approve";

    private static readonly string UnknownApprove =
        $"/api/v1/nao-existe/organizer-applications/{Guid.CreateVersion7()}/approve";

    [Fact]
    public async Task WithoutSessionTheAdministrationLooksLikeAnUnknownRoute()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        using var client = factory.CreateDefaultClient(
            new ClockCookieHandler(factory.Services.GetRequiredService<TimeProvider>()));

        await AssertLooksUnknownAsync(client, cancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ApplicationRole.Organizer)]
    [InlineData(ApplicationRole.DemoViewer)]
    public async Task ForAnyOtherAccountTheAdministrationLooksLikeAnUnknownRoute(string? role)
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("nao-admin");
        await CreateUserAsync(factory, email, role);
        using var client = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        await AssertLooksUnknownAsync(client, cancellationToken);
    }

    [Fact]
    public async Task ThePlatformAdminStillSeesTheRealRoutes()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var email = UniqueEmail("admin");
        await CreateUserAsync(factory, email, ApplicationRole.PlatformAdmin);
        using var admin = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);

        using var queue = await admin.GetAsync(new Uri(Queue, UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);

        // O método errado só denuncia a rota para quem já pode vê-la.
        using var wrongMethod = await admin.GetAsync(new Uri(Approve, UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
    }

    /// <summary>
    /// Leitura, escrita sem e com antiforgery e método errado: em todos os casos a rota da
    /// administração e a sua irmã inexistente dão a mesma resposta.
    /// </summary>
    private static async Task AssertLooksUnknownAsync(HttpClient client, CancellationToken cancellationToken)
    {
        await AssertSameAsync(
            await SendAsync(client, HttpMethod.Get, Queue, cancellationToken),
            await SendAsync(client, HttpMethod.Get, UnknownQueue, cancellationToken),
            HttpStatusCode.NotFound,
            cancellationToken);

        await AssertSameAsync(
            await SendAsync(client, HttpMethod.Get, Approve, cancellationToken),
            await SendAsync(client, HttpMethod.Get, UnknownApprove, cancellationToken),
            HttpStatusCode.NotFound,
            cancellationToken);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var withoutToken = await SendAsync(client, HttpMethod.Post, Approve, cancellationToken);
        var unknownWithoutToken = await SendAsync(client, HttpMethod.Post, UnknownApprove, cancellationToken);
        Assert.Equal(unknownWithoutToken.StatusCode, withoutToken.StatusCode);
        await AssertSameAsync(withoutToken, unknownWithoutToken, unknownWithoutToken.StatusCode, cancellationToken);

        await RefreshAntiforgeryAsync(client, cancellationToken);
        var withToken = await SendAsync(client, HttpMethod.Post, Approve, cancellationToken);
        var unknownWithToken = await SendAsync(client, HttpMethod.Post, UnknownApprove, cancellationToken);
        await AssertSameAsync(withToken, unknownWithToken, unknownWithToken.StatusCode, cancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, withToken.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { reason = "Tentativa de quem não administra." });
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task AssertSameAsync(
        HttpResponseMessage concealed,
        HttpResponseMessage unknown,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using (concealed)
        using (unknown)
        {
            Assert.Equal(expected, unknown.StatusCode);
            Assert.Equal(unknown.StatusCode, concealed.StatusCode);
            Assert.Equal(
                unknown.Content.Headers.ContentType?.MediaType,
                concealed.Content.Headers.ContentType?.MediaType);
            Assert.Empty(concealed.Content.Headers.Allow);

            var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var concealedBody = await concealed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            foreach (var field in new[] { "type", "title", "status", "detail", "code" })
            {
                Assert.Equal(
                    unknownBody.TryGetProperty(field, out var a) ? a.ToString() : null,
                    concealedBody.TryGetProperty(field, out var b) ? b.ToString() : null);
            }
        }
    }
}
