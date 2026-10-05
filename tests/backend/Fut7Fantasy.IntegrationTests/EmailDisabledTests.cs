using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Publicação sem e-mail (Fase 17, 04 §11): a demonstração na Azure não tem provedor de
/// e-mail. A API sobe sem SMTP, avisa a interface, e o cadastro e a recuperação de senha,
/// que só funcionam por e-mail, respondem como rotas inexistentes.
/// </summary>
public sealed class EmailDisabledTests
{
    private static readonly Uri Demo = new("/api/v1/auth/demo", UriKind.Relative);

    [Fact]
    public async Task WithoutEmailTheFlowsThatOnlyWorkByEmailDoNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:Enabled", "false");
            builder.UseSetting("Email:Host", string.Empty);
            builder.UseSetting("Email:FromAddress", string.Empty);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);

        var entrada = await client.GetFromJsonAsync<JsonElement>(Demo, cancellationToken);
        Assert.False(entrada.GetProperty("selfService").GetBoolean());

        using var cadastro = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email = "quem-chega@example.test", displayName = "Quem chega", password = SenhaValida },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, cadastro.StatusCode);

        using var recuperacao = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/forgot-password", UriKind.Relative),
            new { email = "quem-chega@example.test" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, recuperacao.StatusCode);
    }

    [Fact]
    public async Task WithEmailTheInterfaceOffersAccountCreation()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var entrada = await client.GetFromJsonAsync<JsonElement>(Demo, TestContext.Current.CancellationToken);

        Assert.True(entrada.GetProperty("selfService").GetBoolean());
    }

    [Fact]
    public void WithEmailOnTheApiRefusesToStartWithoutTheSmtpServer()
    {
        var recusa = ApiFactory.RefusesToStart(() =>
            new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("Email:Host", string.Empty)));

        Assert.Contains("Email:Host", recusa.Message, StringComparison.Ordinal);
    }
}
