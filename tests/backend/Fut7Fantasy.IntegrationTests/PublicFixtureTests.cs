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
/// Calendário e súmula pelas rotas públicas (Fase 8).
///
/// O que está em jogo aqui é uma regra só: fato publicado é público, fato em edição não
/// é. O placar e a súmula aparecem quando a rodada publica, somem quando ela volta para
/// correção, e voltam na republicação.
/// </summary>
public sealed class PublicFixtureTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task ScoreAndSheetFollowThePublicationOfTheRound()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);
        using var visitante = factory.CreateClient();

        // Antes da súmula, o calendário existe e não promete resultado nenhum.
        var antes = await FixturesAsync(visitante, world.Slug, cancellationToken);
        var rodada = antes.GetProperty("rounds")[0];
        Assert.False(rodada.GetProperty("resultPublished").GetBoolean());
        Assert.False(rodada.GetProperty("underCorrection").GetBoolean());
        var partida = rodada.GetProperty("matches")[0];
        Assert.Equal(JsonValueKind.Null, partida.GetProperty("homeScore").ValueKind);
        Assert.False(partida.GetProperty("hasSheet").GetBoolean());
        var matchId = partida.GetProperty("id").GetGuid();

        // Súmula lançada, mas rodada ainda em conferência: continua fora do ar.
        // Três dias, como nos outros cenários: o mercado precisa ter fechado para a
        // súmula aceitar escrita, senão a rodada recusa a edição com 409.
        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 2);
        await SendToReviewAsync(world, world.RoundId, cancellationToken);

        var emConferencia = await FixturesAsync(visitante, world.Slug, cancellationToken);
        var naConferencia = emConferencia.GetProperty("rounds")[0].GetProperty("matches")[0];
        Assert.Equal(JsonValueKind.Null, naConferencia.GetProperty("homeScore").ValueKind);
        using (var recusada = await visitante.GetAsync(Match(world.Slug, matchId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, recusada.StatusCode);
        }

        // Publicada: placar e súmula ficam públicos de uma vez.
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var publicado = await FixturesAsync(visitante, world.Slug, cancellationToken);
        var publicada = publicado.GetProperty("rounds")[0];
        Assert.True(publicada.GetProperty("resultPublished").GetBoolean());
        Assert.True(publicada.GetProperty("provisional").GetBoolean());
        var comPlacar = publicada.GetProperty("matches")[0];
        Assert.Equal(2, comPlacar.GetProperty("homeScore").GetInt32());
        Assert.Equal(0, comPlacar.GetProperty("awayScore").GetInt32());
        Assert.True(comPlacar.GetProperty("hasSheet").GetBoolean());

        var sumula = await visitante.GetFromJsonAsync<JsonElement>(
            Match(world.Slug, matchId), cancellationToken);
        Assert.Equal(2, sumula.GetProperty("homeScore").GetInt32());
        var times = sumula.GetProperty("teams").EnumerateArray().ToList();
        Assert.Equal(2, times.Count);
        Assert.True(times[0].GetProperty("isHome").GetBoolean());

        // Quem marcou aparece primeiro no time dele, com os dois gols.
        var atacante = times[0].GetProperty("athletes")[0];
        Assert.Equal(2, atacante.GetProperty("goals").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(atacante.GetProperty("sportingName").GetString()));

        // Só entra o que o 01 §11 considera público de um atleta.
        var corpo = sumula.ToString();
        Assert.DoesNotContain("@", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("athleteId", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", corpo, StringComparison.OrdinalIgnoreCase);

        // Reaberta para correção: o resultado sai do ar em vez de mostrar meia edição.
        var versao = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using (var reaberta = await ReopenAsync(
            world.Owner,
            world,
            world.RoundId,
            versao,
            "A súmula chegou com um gol a menos.",
            cancellationToken))
        {
            reaberta.EnsureSuccessStatusCode();
        }

        var emCorrecao = await FixturesAsync(visitante, world.Slug, cancellationToken);
        var corrigindo = emCorrecao.GetProperty("rounds")[0];
        Assert.True(corrigindo.GetProperty("underCorrection").GetBoolean());
        Assert.False(corrigindo.GetProperty("resultPublished").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            corrigindo.GetProperty("matches")[0].GetProperty("homeScore").ValueKind);
        using (var durante = await visitante.GetAsync(Match(world.Slug, matchId), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, durante.StatusCode);
        }

        // Republicada: volta, com o placar novo.
        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 3);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);
        var refeito = await FixturesAsync(visitante, world.Slug, cancellationToken);
        Assert.Equal(
            3,
            refeito.GetProperty("rounds")[0].GetProperty("matches")[0].GetProperty("homeScore").GetInt32());
    }

    [Fact]
    public async Task DraftCompetitionAndUnknownMatchAnswerAsNotFound()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        using var visitante = factory.CreateClient();

        using (var inexistente = await visitante.GetAsync(
            new Uri("/api/v1/public/competitions/campeonato-que-nao-existe/fixtures", UriKind.Relative),
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        }

        using (var semSumula = await visitante.GetAsync(
            Match(world.Slug, Guid.CreateVersion7()), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, semSumula.StatusCode);
        }
    }

    private static Uri Match(string slug, Guid matchId) =>
        new($"/api/v1/public/competitions/{slug}/matches/{matchId}", UriKind.Relative);

    private static async Task<JsonElement> FixturesAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/public/competitions/{slug}/fixtures", UriKind.Relative),
            cancellationToken);

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
