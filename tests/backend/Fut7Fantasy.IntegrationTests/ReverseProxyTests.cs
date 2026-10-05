using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Atrás do balanceador da Azure (Fase 17): o TLS termina antes da API, que recebe HTTP
/// com <c>X-Forwarded-Proto</c> e <c>X-Forwarded-For</c>. A publicação liga
/// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>, o interruptor do próprio ASP.NET Core para
/// isso. Sem ele, todo cliente chegaria com o IP do balanceador, e o antiforgery, que
/// exige HTTPS fora do desenvolvimento, derrubaria com 500 cada tela que pede o token.
/// </summary>
public sealed class ReverseProxyTests
{
    private static readonly Uri Antiforgery = new("/api/v1/auth/antiforgery", UriKind.Relative);
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    [Fact]
    public async Task HttpsEndedAtTheProxyStillCountsAsHttps()
    {
        using var factory = BehindTheProxy();

        // Host público: o ASP.NET Core nunca manda HSTS para localhost.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://cartola.example.test"),
        });
        using var pedido = new HttpRequestMessage(HttpMethod.Get, Antiforgery);
        pedido.Headers.Add("X-Forwarded-Proto", "https");

        using var resposta = await client.SendAsync(pedido, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.True(resposta.Headers.Contains("Strict-Transport-Security"));
        Assert.All(
            resposta.Headers.GetValues("Set-Cookie"),
            cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EachClientBehindTheProxyHasItsOwnLoginQuota()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = BehindTheProxy();
        using var client = factory.CreateClient();

        // A cota de produção (10 em 15 minutos, do ApiFactory) esgota para um cliente...
        for (var tentativa = 0; tentativa < 10; tentativa++)
        {
            using var permitida = await LoginFromAsync(client, "203.0.113.10", cancellationToken);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, permitida.StatusCode);
        }

        using var esgotada = await LoginFromAsync(client, "203.0.113.10", cancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, esgotada.StatusCode);

        // ...e não para quem chega por outro endereço através do mesmo balanceador.
        using var outroCliente = await LoginFromAsync(client, "198.51.100.20", cancellationToken);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, outroCliente.StatusCode);
    }

    private static WebApplicationFactory<Program> BehindTheProxy() =>
        new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ForwardedHeaders_Enabled", "true");
        });

    private static async Task<HttpResponseMessage> LoginFromAsync(
        HttpClient client,
        string clientAddress,
        CancellationToken cancellationToken)
    {
        // Com corpo JSON, como o SPA manda: sem ele o roteamento nem escolhe o login, que só
        // aceita JSON, e a cota dele não entra na conta.
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Login)
        {
            Content = JsonContent.Create(new { email = "quem-tenta@example.test", password = SenhaValida }),
        };
        pedido.Headers.Add("X-Forwarded-For", clientAddress);
        pedido.Headers.Add("X-Forwarded-Proto", "https");
        return await client.SendAsync(pedido, cancellationToken);
    }
}
