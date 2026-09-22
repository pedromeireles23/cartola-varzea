using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.FantasyScenario;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Ligas privadas pela API real (Fase 11): duas contas na mesma liga veem o mesmo
/// ranking acumulado, o código só vale enquanto o dono quiser, e nada do que uma liga
/// devolve alcança quem não está nela.
/// </summary>
public sealed class LeagueFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task TwoAccountsInTheSameLeagueSeeTheSameAccumulatedRanking()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        using var dona = await PlayerAsync(factory, world, "liga-dona", "Pessoa Dona", cancellationToken);
        using var convidada = await PlayerAsync(
            factory, world, "liga-convidada", "Pessoa Convidada", cancellationToken);

        // Criar exige já jogar o campeonato: liga é um recorte de quem disputa.
        using var deFora = await OutsiderAsync(factory, cancellationToken);
        using (var recusada = await CreateLeagueAsync(deFora, world.Slug, "Liga de Fora", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, recusada.StatusCode);
            Assert.Contains("Entre no campeonato", await recusada.Content.ReadAsStringAsync(cancellationToken));
        }

        using var criada = await CreateLeagueAsync(dona, world.Slug, "Liga da Firma", cancellationToken);
        criada.EnsureSuccessStatusCode();
        var liga = await criada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var ligaId = liga.GetProperty("id").GetGuid();
        var codigo = liga.GetProperty("inviteCode").GetString()!;
        Assert.Equal(10, codigo.Length);
        Assert.Equal(1, liga.GetProperty("members").GetInt32());
        Assert.True(liga.GetProperty("isOwner").GetBoolean());

        // O código não vaza para quem não é dono, nem na lista nem na liga aberta.
        using (var entrou = await JoinAsync(convidada, codigo, cancellationToken))
        {
            entrou.EnsureSuccessStatusCode();
            var vista = await entrou.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal(ligaId, vista.GetProperty("id").GetGuid());
            Assert.False(vista.GetProperty("isOwner").GetBoolean());
            Assert.Equal(JsonValueKind.Null, vista.GetProperty("inviteCode").ValueKind);
            Assert.Equal(2, vista.GetProperty("members").GetInt32());
        }

        // Usar o código de novo não duplica: devolve a liga, que é o que se queria ver.
        using (var denovo = await JoinAsync(convidada, codigo, cancellationToken))
        {
            denovo.EnsureSuccessStatusCode();
            Assert.Equal(
                2,
                (await denovo.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
                    .GetProperty("members").GetInt32());
        }

        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        // As duas contas leem a mesma liga, com os mesmos números.
        var pelaDona = await LeagueAsync(dona, world.Slug, ligaId, cancellationToken);
        var pelaConvidada = await LeagueAsync(convidada, world.Slug, ligaId, cancellationToken);
        var linhasDona = pelaDona.GetProperty("members").EnumerateArray().ToList();
        var linhasConvidada = pelaConvidada.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, linhasDona.Count);
        Assert.Equal(
            linhasDona.Select(linha => linha.GetProperty("displayName").GetString()),
            linhasConvidada.Select(linha => linha.GetProperty("displayName").GetString()));
        Assert.Equal(
            linhasDona.Select(linha => linha.GetProperty("totalPoints").GetDecimal()),
            linhasConvidada.Select(linha => linha.GetProperty("totalPoints").GetDecimal()));
        Assert.Equal(1, pelaDona.GetProperty("rounds").GetInt32());
        Assert.Equal("Rodada 1", pelaDona.GetProperty("lastRoundName").GetString());

        // Cada uma se reconhece na própria leitura, e só ela.
        Assert.Single(linhasDona, linha => linha.GetProperty("isViewer").GetBoolean());
        Assert.Single(linhasDona, linha => linha.GetProperty("isOwner").GetBoolean());
        Assert.Equal(
            "Pessoa Dona",
            linhasDona.Single(linha => linha.GetProperty("isViewer").GetBoolean())
                .GetProperty("displayName").GetString());
        Assert.Equal(
            "Pessoa Convidada",
            linhasConvidada.Single(linha => linha.GetProperty("isViewer").GetBoolean())
                .GetProperty("displayName").GetString());

        // A dona pode remover qualquer linha; a convidada só a própria.
        Assert.All(
            linhasDona,
            linha => Assert.NotEqual(JsonValueKind.Null, linha.GetProperty("membershipId").ValueKind));
        Assert.Single(
            linhasConvidada,
            linha => linha.GetProperty("membershipId").ValueKind != JsonValueKind.Null);

        // Quem não é membro não sabe nem que a liga existe.
        using (var espiando = await deFora.GetAsync(
            new System.Uri($"/api/v1/fantasy/{world.Slug}/leagues/{ligaId}", UriKind.Relative), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, espiando.StatusCode);
        }
    }

    [Fact]
    public async Task InviteStopsWorkingWhenTheOwnerRotatesOrClosesIt()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        using var dona = await PlayerAsync(factory, world, "rotacao-dona", "Dona", cancellationToken);
        using var outra = await PlayerAsync(factory, world, "rotacao-outra", "Outra", cancellationToken);

        using var criada = await CreateLeagueAsync(dona, world.Slug, "Liga Rotativa", cancellationToken);
        criada.EnsureSuccessStatusCode();
        var liga = await criada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var ligaId = liga.GetProperty("id").GetGuid();
        var antigo = liga.GetProperty("inviteCode").GetString()!;
        var versao = liga.GetProperty("version").GetString()!;

        // Só o dono troca o código.
        using (var porOutra = await outra.PutAsJsonAsync(
            $"/api/v1/fantasy/{world.Slug}/leagues/{ligaId}/invite",
            new { version = versao, close = false },
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, porOutra.StatusCode);
        }

        using var girada = await dona.PutAsJsonAsync(
            $"/api/v1/fantasy/{world.Slug}/leagues/{ligaId}/invite",
            new { version = versao, close = false },
            cancellationToken);
        girada.EnsureSuccessStatusCode();
        var depois = await girada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var novo = depois.GetProperty("inviteCode").GetString()!;
        Assert.NotEqual(antigo, novo);

        // O código antigo não abre mais a porta, e a resposta é a mesma de um inexistente.
        using (var comAntigo = await JoinAsync(outra, antigo, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, comAntigo.StatusCode);
        }

        using (var inventado = await JoinAsync(outra, "ZZZZZZZZZZ", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, inventado.StatusCode);
        }

        // Um texto que nem pode ser código morre antes de chegar ao banco.
        using (var malformado = await JoinAsync(outra, "abc", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, malformado.StatusCode);
        }

        using (var comNovo = await JoinAsync(outra, novo, cancellationToken))
        {
            comNovo.EnsureSuccessStatusCode();
        }

        // Fechar a liga para de aceitar gente sem mexer em quem já está dentro.
        var atual = (await LeagueAsync(dona, world.Slug, ligaId, cancellationToken))
            .GetProperty("version").GetString()!;
        using (var fechada = await dona.PutAsJsonAsync(
            $"/api/v1/fantasy/{world.Slug}/leagues/{ligaId}/invite",
            new { version = atual, close = true },
            cancellationToken))
        {
            fechada.EnsureSuccessStatusCode();
            Assert.Equal(
                JsonValueKind.Null,
                (await fechada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
                    .GetProperty("inviteCode").ValueKind);
        }

        Assert.Equal(
            2,
            (await LeagueAsync(dona, world.Slug, ligaId, cancellationToken))
                .GetProperty("members").EnumerateArray().Count());
    }

    [Fact]
    public async Task OwnerRemovesAnyoneAndAnyoneLeavesButTheOwnerDeletesInstead()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi(new FakeTimeProvider(ApiFactory.FixedNow));
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        using var dona = await PlayerAsync(factory, world, "saida-dona", "Dona", cancellationToken);
        using var membro = await PlayerAsync(factory, world, "saida-membro", "Membro", cancellationToken);

        using var criada = await CreateLeagueAsync(dona, world.Slug, "Liga que Esvazia", cancellationToken);
        criada.EnsureSuccessStatusCode();
        var liga = await criada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var ligaId = liga.GetProperty("id").GetGuid();
        using (var entrou = await JoinAsync(membro, liga.GetProperty("inviteCode").GetString(), cancellationToken))
        {
            entrou.EnsureSuccessStatusCode();
        }

        var linhas = (await LeagueAsync(dona, world.Slug, ligaId, cancellationToken))
            .GetProperty("members").EnumerateArray().ToList();
        var daDona = linhas.Single(linha => linha.GetProperty("isOwner").GetBoolean())
            .GetProperty("membershipId").GetGuid();
        var doMembro = linhas.Single(linha => !linha.GetProperty("isOwner").GetBoolean())
            .GetProperty("membershipId").GetGuid();

        // O membro não remove a dona.
        using (var tentativa = await membro.DeleteAsync(
            Membro(world.Slug, ligaId, daDona), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tentativa.StatusCode);
        }

        // A dona não sai da própria liga: liga sem dono não existe.
        using (var tentativa = await dona.DeleteAsync(
            Membro(world.Slug, ligaId, daDona), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tentativa.StatusCode);
        }

        // O membro sai sozinho.
        using (var saiu = await membro.DeleteAsync(
            Membro(world.Slug, ligaId, doMembro), cancellationToken))
        {
            saiu.EnsureSuccessStatusCode();
        }

        Assert.Empty(await MineAsync(membro, world.Slug, cancellationToken));
        Assert.Single(
            (await LeagueAsync(dona, world.Slug, ligaId, cancellationToken))
                .GetProperty("members").EnumerateArray());

        // Apagar a liga é a saída do dono, e leva as associações junto.
        var versao = (await LeagueAsync(dona, world.Slug, ligaId, cancellationToken))
            .GetProperty("version").GetString()!;
        using (var apagada = await dona.DeleteAsync(
            new System.Uri(
                $"/api/v1/fantasy/{world.Slug}/leagues/{ligaId}?version={System.Uri.EscapeDataString(versao)}",
                UriKind.Relative),
            cancellationToken))
        {
            apagada.EnsureSuccessStatusCode();
        }

        Assert.Empty(await MineAsync(dona, world.Slug, cancellationToken));
    }

    [Fact]
    public async Task LeagueRoutesRefuseASlugThatIsNotTheCompetitionOfTheLeague()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        using var dona = await PlayerAsync(factory, world, "liga-slug", "Pessoa Dona", cancellationToken);
        using var criada = await CreateLeagueAsync(dona, world.Slug, "Liga do Slug", cancellationToken);
        criada.EnsureSuccessStatusCode();
        var liga = await criada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var leagueId = liga.GetProperty("id").GetGuid();
        var version = liga.GetProperty("version").GetString();

        // O slug da rota descreve o que ela devolve: sob outro campeonato, a mesma liga
        // responde como inexistente, sem confirmar onde ela está de verdade.
        const string OutroSlug = "campeonato-que-nao-e-o-da-liga";
        using (var lida = await dona.GetAsync(
            new System.Uri($"/api/v1/fantasy/{OutroSlug}/leagues/{leagueId}", UriKind.Relative),
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, lida.StatusCode);
        }

        using (var trocada = await dona.PutAsJsonAsync(
            $"/api/v1/fantasy/{OutroSlug}/leagues/{leagueId}/invite",
            new { version, close = false },
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, trocada.StatusCode);
        }

        var versao = System.Uri.EscapeDataString(version!);
        using (var apagada = await dona.DeleteAsync(
            new System.Uri(
                $"/api/v1/fantasy/{OutroSlug}/leagues/{leagueId}?version={versao}", UriKind.Relative),
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, apagada.StatusCode);
        }

        // E nada disso mexeu na liga: pelo endereço certo ela continua inteira.
        var intacta = await LeagueAsync(dona, world.Slug, leagueId, cancellationToken);
        Assert.Equal("Liga do Slug", intacta.GetProperty("name").GetString());
        Assert.Single(intacta.GetProperty("members").EnumerateArray());
    }

    private static System.Uri Membro(string slug, Guid leagueId, Guid membershipId) =>
        new($"/api/v1/fantasy/{slug}/leagues/{leagueId}/members/{membershipId}", UriKind.Relative);

    private static Task<HttpResponseMessage> CreateLeagueAsync(
        HttpClient client,
        string slug,
        string name,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync($"/api/v1/fantasy/{slug}/leagues", new { name }, cancellationToken);

    private static Task<HttpResponseMessage> JoinAsync(
        HttpClient client,
        string? code,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            new System.Uri(
                $"/api/v1/league-invites/{System.Uri.EscapeDataString(code ?? "-")}/accept",
                UriKind.Relative),
            null,
            cancellationToken);

    private static async Task<JsonElement> LeagueAsync(
        HttpClient client,
        string slug,
        Guid leagueId,
        CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/fantasy/{slug}/leagues/{leagueId}", cancellationToken);

    private static async Task<List<JsonElement>> MineAsync(
        HttpClient client,
        string slug,
        CancellationToken cancellationToken) =>
        [
            .. (await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/fantasy/{slug}/leagues", cancellationToken)).EnumerateArray(),
        ];

    /// <summary>Conta que já joga o campeonato, que é o que a liga exige.</summary>
    private static async Task<HttpClient> PlayerAsync(
        WebApplicationFactory<Program> factory,
        World world,
        string prefixo,
        string nome,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail(prefixo);
        await CreateUserAsync(factory, email, displayName: nome);
        var client = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);
        await JoinWithCompleteSquadAsync(client, world.Slug, cancellationToken);
        return client;
    }

    /// <summary>Conta com sessão, mas sem equipe em campeonato nenhum.</summary>
    private static async Task<HttpClient> OutsiderAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail("liga-de-fora");
        await CreateUserAsync(factory, email, displayName: "Pessoa de Fora");
        return await CreateAuthenticatedClientAsync(factory, email, cancellationToken);
    }

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
