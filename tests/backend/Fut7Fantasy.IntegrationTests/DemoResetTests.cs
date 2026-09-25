using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Critérios de saída da Fase 12: o ambiente de demonstração é recriado do zero sem mão
/// humana, o mesmo reset termina no mesmo campeonato, e a credencial pública não altera
/// dado nenhum.
/// </summary>
public sealed class DemoResetTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string Database = "Fut7Fantasy_Demo_Testes";
    private const string SenhaDoVisitante = "quem-assiste-le-tudo-2026";

    [Fact]
    public async Task ResetRebuildsTheSameCompetitionFromScratchAndTheViewerOnlyReads()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var connection = new SqlConnectionStringBuilder(sqlServer.ConnectionString) { InitialCatalog = Database }
            .ConnectionString;

        // Do zero: o banco nem existe antes do primeiro reset.
        var summary = await ResetAsync(connection, cancellationToken);
        Assert.Equal("copa-da-vila-2026", summary.Slug);

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            ApiFactory.ApplyRequiredSettings(builder, connection));
        using var visitante = factory.CreateClient();
        var publico = $"/api/v1/public/competitions/{summary.Slug}";
        var primeiro = await RankingAsync(visitante, publico, cancellationToken);

        // O mesmo reset de novo termina no mesmo campeonato, com a API no ar.
        await ResetAsync(connection, cancellationToken);
        Assert.Equal(primeiro, await RankingAsync(visitante, publico, cancellationToken));

        // A história: turno com rodadas em todos os estados, e o returno em rascunho.
        var calendario = await visitante.GetFromJsonAsync<JsonElement>(
            new System.Uri($"{publico}/fixtures", UriKind.Relative), cancellationToken);
        var fases = calendario.GetProperty("rounds").EnumerateArray()
            .Select(rodada => rodada.GetProperty("phase").GetString())
            .ToList();
        Assert.Equal(
            ["Consolidated", "Consolidated", "Published", "UnderReview", "MarketOpen", "Draft"],
            fases);
        Assert.Equal(13, primeiro.Count);
        Assert.Contains(primeiro, linha => linha.StartsWith("Visitante da demo", StringComparison.Ordinal));

        var tabela = await visitante.GetFromJsonAsync<JsonElement>(
            new System.Uri($"{publico}/standings", UriKind.Relative), cancellationToken);
        var times = tabela.GetProperty("stages")[0].GetProperty("groups")[0].GetProperty("rows")
            .EnumerateArray()
            .ToList();
        Assert.Equal(6, times.Count);
        Assert.All(times, time => Assert.Equal(3, time.GetProperty("played").GetInt32()));

        // A conta pública entra, vê o jogo e a organização, e não escreve nada.
        using var conta = await EntrarAsync(factory, DemoUniverse.Viewer.Email, cancellationToken);
        var campeonatos = await conta.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/fantasy", UriKind.Relative), cancellationToken);
        Assert.Equal(1, campeonatos.GetArrayLength());
        var organizacoes = await conta.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/organizations/mine", UriKind.Relative), cancellationToken);
        Assert.Equal("Assistant", organizacoes[0].GetProperty("role").GetString());
        var avisos = await conta.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/notifications", UriKind.Relative), cancellationToken);
        Assert.Contains(
            avisos.GetProperty("items").EnumerateArray(),
            aviso => aviso.GetProperty("title").GetString() == "Rodada 2 corrigida");

        using var escrita = await conta.PostAsJsonAsync(
            new System.Uri($"/api/v1/fantasy/{summary.Slug}/leagues", UriKind.Relative),
            new { name = "Liga que não nasce" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
        var recusa = await escrita.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("demo_read_only", recusa.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Fut7Fantasy", "Fut7Fantasy")]
    [InlineData("Fut7Fantasy_E2E", "Fut7Fantasy_E2E")]
    [InlineData("Fut7Fantasy_Demo", "Fut7Fantasy_Demo_Outro")]
    [InlineData("", "")]
    public void ResetRefusesAnyDatabaseThatIsNotTheConfirmedDemo(string actual, string confirmed)
    {
        var recusa = Assert.Throws<InvalidOperationException>(() => DemoReset.EnsureDemoDatabase(actual, confirmed));
        Assert.Contains("Nada foi apagado", recusa.Message, StringComparison.Ordinal);
    }

    private static async Task<DemoSummary> ResetAsync(string connection, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connection,
            ["Demo:ViewerPassword"] = SenhaDoVisitante,
            ["Demo:OrganizerPassword"] = "quem-apura-publica-2026",
            ["Demo:AdminPassword"] = "quem-aprova-decide-2026",
        });
        builder.Services.AddDemo(builder.Configuration, TimeProvider.System.GetUtcNow());
        using var host = builder.Build();
        return await DemoReset.RunAsync(host.Services, Database, cancellationToken);
    }

    private static async Task<List<string>> RankingAsync(
        HttpClient client,
        string publico,
        CancellationToken cancellationToken)
    {
        var ranking = await client.GetFromJsonAsync<JsonElement>(
            new System.Uri($"{publico}/ranking", UriKind.Relative), cancellationToken);
        return
        [
            .. ranking.GetProperty("entries").EnumerateArray().Select(linha =>
                $"{linha.GetProperty("displayName").GetString()} {linha.GetProperty("totalPoints").GetDecimal()}"),
        ];
    }

    private static async Task<HttpClient> EntrarAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RefreshAntiforgeryAsync(client, cancellationToken);
        using var login = await client.PostAsJsonAsync(
            new System.Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = SenhaDoVisitante },
            cancellationToken);
        login.EnsureSuccessStatusCode();
        await RefreshAntiforgeryAsync(client, cancellationToken);
        return client;
    }
}
