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
/// Critério de saída da Fase 8: nenhuma resposta pública vaza e-mail, identificador
/// sensível ou dado interno. Cada rota pública já prova o próprio limite; aqui a
/// superfície inteira é percorrida de uma vez, como um visitante faria, num campeonato
/// com participantes, liga e rodada apurada — que é quando há mais a esconder.
/// </summary>
public sealed class PublicSurfaceTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    /// <summary>
    /// Propriedades que não têm o que fazer numa resposta pública. Identificador de time,
    /// atleta, rodada e partida pode sair — é por ele que a navegação pública anda, e ele
    /// não é adivinhável —, mas conta, organização, campeonato por dentro, participação,
    /// liga e convite não. `version` fica de fora da lista porque a versão da regra de
    /// pontuação é pública de propósito; a versão de concorrência é vigiada pelo valor.
    /// </summary>
    private static readonly string[] ForbiddenProperties =
    [
        "email", "userId", "accountId", "ownerId", "organizationId", "competitionId", "entryId",
        "membershipId", "leagueId", "inviteCode", "rowVersion", "createdBy", "updatedBy",
        "passwordHash", "securityStamp", "viewerRole",
    ];

    [Fact]
    public async Task EveryPublicResponseCarriesOnlyWhatIsPublic()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var world = await BuildAsync(factory, cancellationToken);
        await OpenMarketAsync(world.Owner, world, cancellationToken);

        var dono = await world.Owner.GetFromJsonAsync<JsonElement>(
            new System.Uri("/api/v1/auth/me", UriKind.Relative), cancellationToken);

        var email = UniqueEmail("vitrine");
        var participanteId = await CreateUserAsync(factory, email, displayName: "Pessoa Vitrine");
        using var participante = await CreateAuthenticatedClientAsync(factory, email, cancellationToken);
        await JoinWithCompleteSquadAsync(participante, world.Slug, cancellationToken);

        using var criada = await participante.PostAsJsonAsync(
            Uri(world.Slug, "leagues"), new { name = "Liga da Vitrine" }, cancellationToken);
        criada.EnsureSuccessStatusCode();
        var liga = await criada.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        clock.Advance(TimeSpan.FromDays(3));
        await FillSheetAsync(world, world.RoundId, cancellationToken, goals: 2);
        await PublishRoundAsync(world, world.RoundId, cancellationToken);

        var configuracao = await world.Owner.GetFromJsonAsync<JsonElement>(
            new System.Uri($"/api/v1/competitions/{world.CompetitionId}/settings", UriKind.Relative),
            cancellationToken);
        var versaoDaRodada = await RoundVersionAsync(world, world.RoundId, cancellationToken);

        // O que não pode aparecer em lugar nenhum, pelo valor.
        string[] segredos =
        [
            email,
            dono.GetProperty("email").GetString()!,
            participanteId.ToString(),
            dono.GetProperty("id").GetString()!,
            world.OrganizationId.ToString(),
            world.CompetitionId.ToString(),
            liga.GetProperty("id").GetString()!,
            liga.GetProperty("inviteCode").GetString()!,
            liga.GetProperty("version").GetString()!,
            configuracao.GetProperty("version").GetString()!,
            versaoDaRodada,
        ];

        using var visitante = factory.CreateClient();
        var respostas = new Dictionary<string, string>();

        async Task<JsonElement> LerAsync(string caminho)
        {
            var corpo = await visitante.GetStringAsync(new System.Uri(caminho, UriKind.Relative), cancellationToken);
            respostas[caminho] = corpo;
            return JsonDocument.Parse(corpo).RootElement.Clone();
        }

        var publico = $"/api/v1/public/competitions/{world.Slug}";
        await LerAsync("/api/v1/public/scoring-rules");
        await LerAsync("/api/v1/public/competitions?busca=Copa");
        var campeonato = await LerAsync(publico);
        await LerAsync($"{publico}/ranking");
        await LerAsync($"{publico}/standings");
        var calendario = await LerAsync($"{publico}/fixtures");

        foreach (var time in campeonato.GetProperty("teams").EnumerateArray())
        {
            var elenco = await LerAsync($"{publico}/teams/{time.GetProperty("id").GetGuid()}");
            foreach (var atleta in elenco.GetProperty("athletes").EnumerateArray())
            {
                await LerAsync($"{publico}/athletes/{atleta.GetProperty("id").GetGuid()}");
            }
        }

        var sumulas = calendario.GetProperty("rounds").EnumerateArray()
            .SelectMany(rodada => rodada.GetProperty("matches").EnumerateArray())
            .Where(partida => partida.GetProperty("hasSheet").GetBoolean())
            .ToList();
        Assert.NotEmpty(sumulas);
        foreach (var partida in sumulas)
        {
            await LerAsync($"{publico}/matches/{partida.GetProperty("id").GetGuid()}");
        }

        // A varredura precisa ter andado de verdade: seis times, os atletas deles e a súmula.
        Assert.True(respostas.Count > 50, $"Só {respostas.Count} respostas públicas foram lidas.");
        Assert.Contains("Pessoa Vitrine", respostas[$"{publico}/ranking"], StringComparison.Ordinal);

        foreach (var (caminho, corpo) in respostas)
        {
            // Nenhum e-mail, de ninguém.
            Assert.False(corpo.Contains('@', StringComparison.Ordinal), $"{caminho} tem um '@': {corpo}");
            foreach (var segredo in segredos)
            {
                Assert.False(
                    corpo.Contains(segredo, StringComparison.OrdinalIgnoreCase),
                    $"{caminho} devolveu um valor interno ({segredo}).");
            }

            var proibidas = PropertyNames(JsonDocument.Parse(corpo).RootElement)
                .Where(nome => ForbiddenProperties.Contains(nome, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Assert.True(proibidas.Count == 0, $"{caminho} tem {string.Join(", ", proibidas)}.");
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement elemento) =>
        elemento.ValueKind switch
        {
            JsonValueKind.Object => elemento.EnumerateObject()
                .SelectMany(propriedade => PropertyNames(propriedade.Value).Prepend(propriedade.Name)),
            JsonValueKind.Array => elemento.EnumerateArray().SelectMany(PropertyNames),
            _ => [],
        };

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
}
