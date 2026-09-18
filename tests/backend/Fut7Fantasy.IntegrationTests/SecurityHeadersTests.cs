using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Cabeçalhos de segurança da borda HTTP (04-seguranca §7).</summary>
public sealed class SecurityHeadersTests
{
    private static readonly Uri PublicHost = new("https://cartola.example.test");

    [Fact]
    public async Task ProducaoExigeHttpsPorHsts()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = PublicHost,
        });

        using var resposta = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(resposta.Headers.TryGetValues("Strict-Transport-Security", out var valores));
        Assert.Equal("max-age=31536000", Assert.Single(valores));
    }

    [Fact]
    public async Task DesenvolvimentoNaoFixaHstsNoNavegador()
    {
        // HSTS no localhost prenderia o navegador de quem desenvolve em HTTPS.
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = PublicHost,
        });

        using var resposta = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(resposta.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal("nosniff", Assert.Single(resposta.Headers.GetValues("X-Content-Type-Options")));
    }
}
