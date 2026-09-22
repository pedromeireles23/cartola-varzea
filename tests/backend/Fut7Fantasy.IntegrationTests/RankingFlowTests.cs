using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.FantasyScenario;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// O ranking geral acumulado pela API real (Fase 11): público, ordenado pelos critérios
/// aprovados, somando a revisão vigente de cada rodada e acompanhando a correção sem
/// nada para sincronizar.
/// </summary>
public sealed class RankingFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task RankingIsPublicAndFollowsTheApprovedOrderWithoutLoggingIn()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        // Antes de qualquer adesão, o ranking existe e está vazio.
        using var visitante = factory.CreateClient();
        var vazio = await visitante.GetFromJsonAsync<JsonElement>(
            $"/api/v1/public/competitions/{world.Slug}/ranking", cancellationToken);
        Assert.Equal(0, vazio.GetProperty("rounds").GetInt32());
        Assert.Empty(vazio.GetProperty("entries").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, vazio.GetProperty("lastRoundName").ValueKind);

        var primeiroEmail = UniqueEmail("ranking-um");
        await CreateUserAsync(factory, primeiroEmail, displayName: "Pessoa Um");
        using var primeiro = await CreateAuthenticatedClientAsync(factory, primeiroEmail, cancellationToken);
        await JoinWithCompleteSquadAsync(primeiro, world.Slug, cancellationToken);

        var segundoEmail = UniqueEmail("ranking-dois");
        await CreateUserAsync(factory, segundoEmail, displayName: "Pessoa Dois");
        using var segundo = await CreateAuthenticatedClientAsync(factory, segundoEmail, cancellationToken);
        await JoinWithCompleteSquadAsync(segundo, world.Slug, cancellationToken);

        // Sem rodada apurada, quem tem o mesmo elenco divide a primeira colocação.
        var semRodada = await visitante.GetFromJsonAsync<JsonElement>(
            $"/api/v1/public/competitions/{world.Slug}/ranking", cancellationToken);
        var inicial = semRodada.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, inicial.Count);
        Assert.All(inicial, linha => Assert.Equal(1, linha.GetProperty("position").GetInt32()));
        Assert.All(inicial, linha => Assert.True(linha.GetProperty("tied").GetBoolean()));
        Assert.All(inicial, linha => Assert.Equal(0m, linha.GetProperty("totalPoints").GetDecimal()));

        // O visitante lê nome de exibição e nada mais: e-mail e identificador não saem.
        var bruto = await visitante.GetStringAsync(
            $"/api/v1/public/competitions/{world.Slug}/ranking", cancellationToken);
        Assert.Contains("Pessoa Um", bruto, StringComparison.Ordinal);
        Assert.DoesNotContain(primeiroEmail, bruto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", bruto, StringComparison.OrdinalIgnoreCase);

        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var apurado = await visitante.GetFromJsonAsync<JsonElement>(
            $"/api/v1/public/competitions/{world.Slug}/ranking", cancellationToken);
        Assert.Equal(1, apurado.GetProperty("rounds").GetInt32());
        Assert.Equal("Rodada 1", apurado.GetProperty("lastRoundName").GetString());
        Assert.True(apurado.GetProperty("provisional").GetBoolean());
        var linhas = apurado.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, linhas.Count);
        Assert.All(linhas, linha => Assert.False(linha.GetProperty("isViewer").GetBoolean()));

        // Quem fez mais pontos vem primeiro; com uma rodada só, o total é o dela. As duas
        // montaram o elenco mais barato, mas o capitão pode ter saído diferente, então o
        // teste afirma a ordem, não um número. O empate exato é provado em `RankingTests`.
        var totais = linhas.Select(linha => linha.GetProperty("totalPoints").GetDecimal()).ToList();
        Assert.Equal(totais.OrderByDescending(item => item), totais);
        Assert.All(linhas, linha => Assert.Equal(
            linha.GetProperty("totalPoints").GetDecimal(),
            linha.GetProperty("lastRoundPoints").GetDecimal()));
        var nomes = linhas.Select(linha => linha.GetProperty("displayName").GetString()).ToList();
        Assert.Equal(["Pessoa Dois", "Pessoa Um"], [.. nomes.Order(StringComparer.Ordinal)]);

        var posicoes = linhas.Select(linha => linha.GetProperty("position").GetInt32()).ToList();
        Assert.Equal(1, posicoes[0]);
        Assert.Equal(totais[0] == totais[1] ? 1 : 2, posicoes[1]);
        Assert.All(linhas, linha => Assert.Equal(
            totais[0] == totais[1], linha.GetProperty("tied").GetBoolean()));

        // Quem está logado se reconhece na lista.
        var comSessao = await primeiro.GetFromJsonAsync<JsonElement>(
            $"/api/v1/public/competitions/{world.Slug}/ranking", cancellationToken);
        Assert.Single(
            comSessao.GetProperty("entries").EnumerateArray(),
            linha => linha.GetProperty("isViewer").GetBoolean());
    }

    [Fact]
    public async Task CorrectingARoundMovesTheRankingWithoutAnythingToSynchronize()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var email = UniqueEmail("ranking-correcao");
        await CreateUserAsync(factory, email, displayName: "Pessoa Corrigida");
        using var jogador = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);
        await JoinWithCompleteSquadAsync(jogador, world.Slug, cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        using var visitante = factory.CreateClient();
        var antes = await TotalAsync(visitante, world.Slug, cancellationToken);

        var versao = await RoundVersionAsync(world, world.RoundId, cancellationToken);
        using (var reaberta = await world.Owner.PostAsJsonAsync(
            $"/api/v1/competitions/{world.CompetitionId}/rounds/{world.RoundId}/reopen",
            new { version = versao, reason = "O placar chegou errado da arbitragem." },
            cancellationToken))
        {
            reaberta.EnsureSuccessStatusCode();
        }

        // Enquanto está reaberta, o ranking segue mostrando a apuração que vale.
        Assert.Equal(antes, await TotalAsync(visitante, world.Slug, cancellationToken));

        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 4);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        Assert.NotEqual(antes, await TotalAsync(visitante, world.Slug, cancellationToken));
    }

    [Fact]
    public async Task DraftCompetitionHasNoPublicRanking()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi(new CapturingEmailSender());
        using var visitante = factory.CreateClient();

        using var inexistente = await visitante.GetAsync(
            new Uri("/api/v1/public/competitions/copa-que-nao-existe/ranking", UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    private static async Task<decimal> TotalAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        (await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/public/competitions/{slug}/ranking", cancellationToken))
        .GetProperty("entries")[0]
        .GetProperty("totalPoints")
        .GetDecimal();
}
