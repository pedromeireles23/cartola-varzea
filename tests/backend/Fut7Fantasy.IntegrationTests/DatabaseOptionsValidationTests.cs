using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.IntegrationTests;

public sealed class DatabaseOptionsValidationTests
{
    [Fact]
    public void ApplicationRefusesToStartWithoutConnectionString()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Database:ConnectionString", string.Empty));

        // Falhar aqui e o comportamento desejado: configuracao invalida derruba a
        // inicializacao, em vez de aparecer como erro na primeira requisicao.
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Database:ConnectionString", exception.Message, StringComparison.Ordinal);
    }
}
