using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Leitura pública: o que o visitante sem conta encontra, o que ele nunca vê e como o
/// campeonato é endereçado fora da área de organização (Fase 5).
/// </summary>
public sealed class PublicCompetitionFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    /// <summary>Elenco mínimo do Fut7: titulares da formação e um reserva por posição.</summary>
    private static readonly (string Position, int Count)[] MinimumSquad =
    [
        ("Goalkeeper", 2),
        ("Defender", 3),
        ("Midfielder", 3),
        ("Forward", 3),
    ];

    /// <summary>Campos da área de organização que não podem atravessar para o público.</summary>
    private static readonly string[] AdministrativeFields =
    [
        "id",
        "organizationId",
        "version",
        "viewerRole",
        "status",
        "createdAt",
        "updatedAt",
        "marketCloseLeadTimeMinutes",
        "resultsSlaBusinessDays",
        "correctionWindowBusinessDays",
        "canChangeModality",
    ];

    [Fact]
    public async Task VisitorOnlyFindsPublishedCompetitionsAndNeverAdministrativeFields()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("public-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Pública");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        // O visitante não tem sessão nem cookie: é o mesmo cliente que um navegador anônimo usa.
        using var visitor = factory.CreateClient();

        var draftId = await CreateCompetitionAsync(owner, organizationId, "Copa Rascunho", cancellationToken);
        var publishedId = await CreateCompetitionAsync(
            owner, organizationId, "Copa da Várzea", cancellationToken);
        await FillCatalogAsync(owner, publishedId, cancellationToken);
        var slug = await PublishAsync(owner, publishedId, cancellationToken);
        Assert.Equal("copa-da-varzea-2026", slug);

        // Os testes desta classe dividem o mesmo banco; a busca filtra pela organização
        // do cenário para que um não enxergue o campeonato do outro.
        var all = await visitor.GetFromJsonAsync<JsonElement>(Search("Liga Pública"), cancellationToken);
        Assert.Equal(["Copa da Várzea"], Names(all));
        Assert.DoesNotContain("Copa Rascunho", Names(all));

        var summary = all[0];
        Assert.Equal("Liga Pública", summary.GetProperty("organizationName").GetString());
        Assert.Equal("Fut7", summary.GetProperty("modality").GetString());
        AssertNoAdministrativeFields(summary);

        var competition = await visitor.GetFromJsonAsync<JsonElement>(Public(slug), cancellationToken);
        Assert.Equal("Copa da Várzea", competition.GetProperty("name").GetString());
        Assert.Equal("Liga Pública", competition.GetProperty("organizationName").GetString());
        Assert.Equal(100, competition.GetProperty("modalityProfile").GetProperty("budget").GetInt32());
        AssertNoAdministrativeFields(competition);

        // A fase publicada mostra grupos e times, que é o que a página pública precisa.
        var stage = competition.GetProperty("stages")[0];
        Assert.Equal("Fase única", stage.GetProperty("name").GetString());
        Assert.Equal(
            ["Alpha", "Beta"],
            stage.GetProperty("groups")[0].GetProperty("teams").EnumerateArray()
                .Select(team => team.GetString()));
        Assert.Equal(
            [("Alpha", 6), ("Beta", 5)],
            competition.GetProperty("teams").EnumerateArray().Select(team => (
                team.GetProperty("name").GetString(),
                team.GetProperty("athletes").GetInt32())));

        // O rascunho não existe para quem está de fora, nem pelo GUID nem por endereço algum.
        using var draftBySlug = await visitor.GetAsync(Public("copa-rascunho-2026"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, draftBySlug.StatusCode);
        using var draftById = await visitor.GetAsync(Public(draftId.ToString()), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, draftById.StatusCode);

        // Voltar para rascunho tira o campeonato do ar sem perder o endereço.
        var readiness = await owner.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/competitions/{publishedId}/readiness", UriKind.Relative), cancellationToken);
        using var unpublished = await owner.PutAsJsonAsync(
            new Uri($"/api/v1/competitions/{publishedId}/publication", UriKind.Relative),
            new { published = false, version = readiness.GetProperty("version").GetString() },
            cancellationToken);
        unpublished.EnsureSuccessStatusCode();

        using var gone = await visitor.GetAsync(Public(slug), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Empty(
            Names(await visitor.GetFromJsonAsync<JsonElement>(Search("Liga Pública"), cancellationToken)));
    }

    [Fact]
    public async Task SearchMatchesNameSeasonAndOrganizationWithoutLettingWildcardsThrough()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("public-search-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga do Interior");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var visitor = factory.CreateClient();

        var first = await CreateCompetitionAsync(owner, organizationId, "Copa do Bairro", cancellationToken);
        await FillCatalogAsync(owner, first, cancellationToken);
        await PublishAsync(owner, first, cancellationToken);
        var second = await CreateCompetitionAsync(
            owner, organizationId, "Torneio de Verão", cancellationToken, season: "2027");
        await FillCatalogAsync(owner, second, cancellationToken);
        await PublishAsync(owner, second, cancellationToken);

        Assert.Equal(["Copa do Bairro"], await SearchNamesAsync(visitor, "bairro", cancellationToken));
        Assert.Equal(["Torneio de Verão"], await SearchNamesAsync(visitor, "verão", cancellationToken));
        Assert.Equal(
            ["Copa do Bairro", "Torneio de Verão"],
            [.. (await SearchNamesAsync(visitor, "Liga do Interior", cancellationToken)).Order()]);
        Assert.Equal(["Torneio de Verão"], await SearchNamesAsync(visitor, "2027", cancellationToken));
        Assert.Empty(await SearchNamesAsync(visitor, "handebol", cancellationToken));

        // Curinga de LIKE é texto para quem busca, não instrução para o banco.
        Assert.Empty(await SearchNamesAsync(visitor, "%", cancellationToken));
        Assert.Empty(await SearchNamesAsync(visitor, "_opa do Bairro", cancellationToken));
        Assert.Empty(await SearchNamesAsync(visitor, "[C]opa", cancellationToken));
    }

    [Fact]
    public async Task CompetitionsWithTheSameNameGetDistinctAddresses()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var firstEmail = UniqueEmail("public-slug-first");
        var secondEmail = UniqueEmail("public-slug-second");
        var firstId = await CreateUserAsync(factory, firstEmail);
        var secondId = await CreateUserAsync(factory, secondEmail);
        var firstOrganization = await CreateOrganizationAsync(factory, firstId, "Liga Norte");
        var secondOrganization = await CreateOrganizationAsync(factory, secondId, "Liga Sul");
        using var first = await CreateAuthenticatedClientAsync(factory, firstEmail, cancellationToken);
        using var second = await CreateAuthenticatedClientAsync(factory, secondEmail, cancellationToken);

        const string Name = "Campeonato Homônimo";
        var firstCompetition = await CreateCompetitionAsync(first, firstOrganization, Name, cancellationToken);
        await FillCatalogAsync(first, firstCompetition, cancellationToken);
        var secondCompetition = await CreateCompetitionAsync(second, secondOrganization, Name, cancellationToken);
        await FillCatalogAsync(second, secondCompetition, cancellationToken);

        Assert.Equal(
            "campeonato-homonimo-2026",
            await PublishAsync(first, firstCompetition, cancellationToken));
        Assert.Equal(
            "campeonato-homonimo-2026-2",
            await PublishAsync(second, secondCompetition, cancellationToken));

        using var visitor = factory.CreateClient();
        var found = await visitor.GetFromJsonAsync<JsonElement>(
            Public("campeonato-homonimo-2026-2"), cancellationToken);
        Assert.Equal("Liga Sul", found.GetProperty("organizationName").GetString());

        // Endereço fora do formato não chega ao banco.
        using var invalid = await visitor.GetAsync(Public("Campeonato Homônimo"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
    }

    private static Uri Search(string? query) => new(
        query is null
            ? "/api/v1/public/competitions"
            : $"/api/v1/public/competitions?busca={Uri.EscapeDataString(query)}",
        UriKind.Relative);

    private static Uri Public(string slug) =>
        new($"/api/v1/public/competitions/{Uri.EscapeDataString(slug)}", UriKind.Relative);

    private static IEnumerable<string?> Names(JsonElement list) =>
        list.EnumerateArray().Select(item => item.GetProperty("name").GetString());

    private static async Task<string?[]> SearchNamesAsync(
        HttpClient visitor,
        string query,
        CancellationToken cancellationToken) =>
        [.. Names(await visitor.GetFromJsonAsync<JsonElement>(Search(query), cancellationToken))];

    private static void AssertNoAdministrativeFields(JsonElement item)
    {
        var present = item.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Empty(present.Intersect(AdministrativeFields));
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient owner,
        Guid organizationId,
        string name,
        CancellationToken cancellationToken,
        string season = "2026")
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name, season, modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Catálogo mínimo para o checklist liberar a publicação.</summary>
    private static async Task FillCatalogAsync(
        HttpClient owner,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var alpha = await CreateTeamAsync(owner, competitionId, "Alpha", cancellationToken);
        var beta = await CreateTeamAsync(owner, competitionId, "Beta", cancellationToken);

        var index = 0;
        foreach (var (position, count) in MinimumSquad)
        {
            for (var number = 1; number <= count; number++)
            {
                using var created = await owner.PostAsJsonAsync(
                    new Uri($"/api/v1/competitions/{competitionId}/athletes", UriKind.Relative),
                    new
                    {
                        sportingName = $"{position} {number}",
                        position,
                        realTeamId = index++ % 2 == 0 ? alpha : beta,
                        priceTier = number == 1 ? "Star" : "Regular",
                    },
                    cancellationToken);
                created.EnsureSuccessStatusCode();
            }
        }

        using var stageCreated = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/competitions/{competitionId}/stages", UriKind.Relative),
            new { name = "Fase única", format = "Groups", groups = new[] { new { name = "Grupo A" } } },
            cancellationToken);
        stageCreated.EnsureSuccessStatusCode();
        var stage = await stageCreated.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var stageId = stage.GetProperty("id").GetGuid();
        var groupId = stage.GetProperty("groups")[0].GetProperty("id").GetGuid();

        using var participants = await owner.PutAsJsonAsync(
            new Uri(
                $"/api/v1/competitions/{competitionId}/stages/{stageId}/participants", UriKind.Relative),
            new
            {
                participants = new[]
                {
                    new { realTeamId = alpha, stageGroupId = groupId },
                    new { realTeamId = beta, stageGroupId = groupId },
                },
                version = stage.GetProperty("version").GetString(),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateTeamAsync(
        HttpClient owner,
        Guid competitionId,
        string name,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/competitions/{competitionId}/teams", UriKind.Relative),
            new { name },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Publica e devolve o endereço público que o campeonato ganhou.</summary>
    private static async Task<string> PublishAsync(
        HttpClient owner,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var readiness = await owner.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/competitions/{competitionId}/readiness", UriKind.Relative), cancellationToken);
        using var published = await owner.PutAsJsonAsync(
            new Uri($"/api/v1/competitions/{competitionId}/publication", UriKind.Relative),
            new { published = true, version = readiness.GetProperty("version").GetString() },
            cancellationToken);
        published.EnsureSuccessStatusCode();
        var body = await published.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("slug").GetString()!;
    }
}
