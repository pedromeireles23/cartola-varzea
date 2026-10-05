using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// A API servindo o build do Angular na mesma origem (Fase 17, 03 §15). Um build de
/// mentira numa pasta temporária faz o papel do <c>wwwroot</c> da publicação.
/// </summary>
public sealed class SpaHostingTests : IDisposable
{
    private const string Bundle = "main-GT4GZAM5.js";

    private readonly DirectoryInfo _build = Directory.CreateTempSubdirectory("fut7fantasy-spa-");
    private readonly WebApplicationFactory<Program> _factory;

    public SpaHostingTests()
    {
        File.WriteAllText(Path.Join(_build.FullName, "index.html"), "<!doctype html><title>SPA</title>");
        File.WriteAllText(Path.Join(_build.FullName, Bundle), "console.info('spa');");

        _factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<StaticFileOptions>(options =>
                options.FileProvider = new PhysicalFileProvider(_build.FullName))));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/c/copa-da-vila-2026/ranking")]
    public async Task AnyRouteOutsideTheApiOpensTheSpaUnderTheSecurityHeaders(string rota)
    {
        using var client = _factory.CreateClient();

        using var resposta = await client.GetAsync(
            new Uri(rota, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("text/html", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "<title>SPA</title>",
            await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        // É aqui que a CSP passa a valer para o HTML: com o ng serve ela só cobria o JSON.
        Assert.Contains(
            "script-src 'self'",
            Assert.Single(resposta.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);

        // O index aponta para os pacotes da versão atual: um deploy novo vale na próxima visita.
        Assert.True(resposta.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task HashedBundleStaysCachedBecauseANewVersionHasANewName()
    {
        using var client = _factory.CreateClient();

        using var resposta = await client.GetAsync(
            new Uri($"/{Bundle}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", resposta.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/api/v1/nao-existe")]
    [InlineData("/api")]
    [InlineData("/chunk-NAOEXIST.js")]
    public async Task UnknownApiRouteOrMissingFileIsNotFoundInsteadOfTheSpa(string rota)
    {
        using var client = _factory.CreateClient();

        using var resposta = await client.GetAsync(
            new Uri(rota, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApiRouteCalledWithTheWrongMethodIsStillMethodNotAllowed()
    {
        using var client = _factory.CreateClient();

        using var resposta = await client.GetAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resposta.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _build.Delete(recursive: true);
    }
}
