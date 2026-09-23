using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.FantasyScenario;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Time e atleta pelas rotas públicas (Fase 8). O que se prova aqui é o limite: sai o
/// que o 01 §11 permite, e a estatística só conta rodada publicada.
/// </summary>
public sealed class PublicCatalogTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task TeamShowsTheSquadAndAthleteStatsFollowPublication()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);
        using var visitante = factory.CreateClient();

        // O campeonato público entrega o identificador do time, que é por onde se chega
        // ao elenco sem nunca expor o GUID na navegação.
        var campeonato = await visitante.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/public/competitions/{world.Slug}", UriKind.Relative), cancellationToken);
        var timeId = campeonato.GetProperty("teams")[0].GetProperty("id").GetGuid();

        var time = await visitante.GetFromJsonAsync<JsonElement>(
            Team(world.Slug, timeId), cancellationToken);
        var elenco = time.GetProperty("athletes").EnumerateArray().ToList();
        Assert.NotEmpty(elenco);
        // Sem nome informado, o técnico é "Técnico do {time}" (01 §7).
        Assert.Contains("Técnico", time.GetProperty("coachName").GetString() ?? string.Empty);
        // O elenco abre pelo gol, que é como uma escalação se lê.
        Assert.Equal("Goalkeeper", elenco[0].GetProperty("position").GetString());

        // Nada de contato nem de documento sai de uma pessoa (01 §11).
        var corpoDoTime = time.ToString();
        Assert.DoesNotContain("@", corpoDoTime, StringComparison.Ordinal);
        Assert.DoesNotContain("birth", corpoDoTime, StringComparison.OrdinalIgnoreCase);

        // Antes de qualquer resultado, o atleta existe e não fez nada ainda.
        var atletaId = elenco[0].GetProperty("id").GetGuid();
        var antes = await visitante.GetFromJsonAsync<JsonElement>(
            Athlete(world.Slug, atletaId), cancellationToken);
        Assert.Equal(0, antes.GetProperty("totals").GetProperty("matches").GetInt32());
        Assert.Empty(antes.GetProperty("priceHistory").EnumerateArray());
        Assert.True(antes.GetProperty("price").GetDecimal() > 0);

        // Súmula lançada e rodada em conferência: a estatística continua fora do ar.
        clock.Advance(TimeSpan.FromDays(3));
        var fatos = await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 2);
        await SendToReviewAsync(world, world.RoundId, cancellationToken);

        var artilheiro = await visitante.GetFromJsonAsync<JsonElement>(
            Athlete(world.Slug, fatos.Scorer), cancellationToken);
        Assert.Equal(0, artilheiro.GetProperty("totals").GetProperty("goals").GetInt32());

        // Publicada: a estatística e o histórico de preço aparecem de uma vez.
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var depois = await visitante.GetFromJsonAsync<JsonElement>(
            Athlete(world.Slug, fatos.Scorer), cancellationToken);
        var totais = depois.GetProperty("totals");
        Assert.Equal(2, totais.GetProperty("goals").GetInt32());
        Assert.Equal(1, totais.GetProperty("matches").GetInt32());
        var historico = depois.GetProperty("priceHistory").EnumerateArray().ToList();
        Assert.Single(historico);
        Assert.Equal("Rodada 1", historico[0].GetProperty("roundName").GetString());
        // Quem fez dois gols joga acima da média da posição e valoriza.
        Assert.True(historico[0].GetProperty("variation").GetDecimal() > 0);

        var corpoDoAtleta = depois.ToString();
        Assert.DoesNotContain("@", corpoDoAtleta, StringComparison.Ordinal);
        Assert.DoesNotContain("userId", corpoDoAtleta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TeamAndAthleteOfAnotherCompetitionAnswerAsNotFound()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        using var visitante = factory.CreateClient();

        using (var time = await visitante.GetAsync(
            Team(world.Slug, Guid.CreateVersion7()), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, time.StatusCode);
        }

        using (var atleta = await visitante.GetAsync(
            Athlete("campeonato-que-nao-existe", Guid.CreateVersion7()), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, atleta.StatusCode);
        }
    }

    private static Uri Team(string slug, Guid teamId) =>
        new($"/api/v1/public/competitions/{slug}/teams/{teamId}", UriKind.Relative);

    private static Uri Athlete(string slug, Guid athleteId) =>
        new($"/api/v1/public/competitions/{slug}/athletes/{athleteId}", UriKind.Relative);

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
