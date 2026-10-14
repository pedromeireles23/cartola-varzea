using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Central de quem organiza: os campeonatos de todas as organizações da conta, com a
/// rodada em jogo e a próxima, numa chamada só.
/// </summary>
public sealed class OrganizerCompetitionsFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private static readonly Uri Mine = new("/api/v1/organizations/mine/competitions", UriKind.Relative);

    [Fact]
    public async Task ListsEveryCompetitionOfTheAccountWithTheRoundInPlayAndTheNextOne()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        // O relógio é o do cenário do jogo: os jogos são marcados a partir dele.
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));

        // Campeonato publicado: a Rodada 1 com o mercado aberto e a Rodada 2 em rascunho.
        var world = await FantasyScenario.BuildAsync(factory, cancellationToken);
        await FantasyScenario.OpenMarketAsync(world.Owner, world, cancellationToken);
        await FantasyScenario.CreateRoundAsync(world.Owner, world, "Rodada 2", daysAhead: 9, cancellationToken);

        // Um rascunho na mesma organização, ainda sem nada.
        using var draft = await world.Owner.PostAsJsonAsync(
            $"/api/v1/organizations/{world.OrganizationId}/competitions",
            new { name = "Copa em Rascunho", season = "2027", modality = "Fut7" },
            cancellationToken);
        draft.EnsureSuccessStatusCode();
        var draftId = (await draft.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("id").GetGuid();

        // Outra organização, de outra pessoa: nada dela pode aparecer.
        var strangerEmail = UniqueEmail("central-stranger");
        var strangerId = await CreateUserAsync(factory, strangerEmail);
        var strangerOrganization = await CreateOrganizationAsync(factory, strangerId, "Liga Alheia");
        using var stranger = await CreateAuthenticatedClientAsync(factory, strangerEmail, cancellationToken);
        using var alien = await stranger.PostAsJsonAsync(
            $"/api/v1/organizations/{strangerOrganization}/competitions",
            new { name = "Copa Alheia", season = "2027", modality = "Fut7" },
            cancellationToken);
        alien.EnsureSuccessStatusCode();

        var list = (await world.Owner.GetFromJsonAsync<JsonElement>(Mine, cancellationToken))
            .EnumerateArray()
            .ToList();
        Assert.Equal(new[] { draftId, world.CompetitionId }.Order(), list.Select(Id).Order());
        Assert.DoesNotContain(list, item => item.GetProperty("name").GetString() == "Copa Alheia");

        var published = list.Single(item => Id(item) == world.CompetitionId);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.Equal("Owner", published.GetProperty("viewerRole").GetString());
        Assert.Equal(world.OrganizationId, published.GetProperty("organizationId").GetGuid());
        Assert.Equal(world.Teams.Count, published.GetProperty("activeTeams").GetInt32());
        Assert.Equal(2, published.GetProperty("matches").GetInt32());

        var current = published.GetProperty("currentRound");
        Assert.Equal("Rodada 1", current.GetProperty("name").GetString());
        Assert.Equal("MarketOpen", current.GetProperty("phase").GetString());
        Assert.Equal(1, current.GetProperty("matches").GetInt32());
        Assert.NotNull(current.GetProperty("marketCloseLocal").GetString());

        var next = published.GetProperty("nextRound");
        Assert.Equal("Rodada 2", next.GetProperty("name").GetString());
        Assert.Equal("Draft", next.GetProperty("phase").GetString());
        Assert.NotNull(next.GetProperty("firstKickoffLocal").GetString());

        var empty = list.Single(item => Id(item) == draftId);
        Assert.Equal("Draft", empty.GetProperty("status").GetString());
        Assert.Equal(0, empty.GetProperty("activeTeams").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("currentRound").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextRound").ValueKind);

        // O auxiliar vê os mesmos campeonatos, com o papel dele.
        var assistantEmail = UniqueEmail("central-assistant");
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        await AddAssistantAsync(factory, world.OrganizationId, assistantId);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var seen = (await assistant.GetFromJsonAsync<JsonElement>(Mine, cancellationToken))
            .EnumerateArray()
            .ToList();
        Assert.Equal(2, seen.Count);
        Assert.All(seen, item => Assert.Equal("Assistant", item.GetProperty("viewerRole").GetString()));

        // Sem conta, nada.
        using var anonymous = factory.CreateClient();
        using var refused = await anonymous.GetAsync(Mine, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
