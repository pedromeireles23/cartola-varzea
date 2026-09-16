using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Campeonato em rascunho, policy contextual de campeonato e edição concorrente.</summary>
public sealed class CompetitionManagementFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task ModalityProfilesArePublicAndMatchTheApprovedRules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var profiles = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/modality-profiles", UriKind.Relative), cancellationToken);

        Assert.Equal(
            ["Fut7", "Futsal", "Field"],
            profiles.EnumerateArray().Select(profile => profile.GetProperty("modality").GetString()));
        var futsal = profiles[1];
        Assert.Equal(80m, futsal.GetProperty("budget").GetDecimal());
        Assert.Equal(5, futsal.GetProperty("starters").GetInt32());
        Assert.Equal(9, futsal.GetProperty("squadAthletes").GetInt32());
        var fieldLimits = profiles[2].GetProperty("realTeamLimits").EnumerateArray()
            .Select(limit => (
                limit.GetProperty("activeRealTeams").GetInt32(),
                limit.GetProperty("maxStarters").GetInt32(),
                limit.GetProperty("maxAthletes").GetInt32()))
            .ToArray();
        Assert.Equal([(4, 4, 6), (3, 6, 8), (2, 8, 11)], fieldLimits);
    }

    [Fact]
    public async Task OwnerCreatesDraftAndOnlyTheOrganizationReachesIt()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("competition-owner");
        var assistantEmail = UniqueEmail("competition-assistant");
        var otherOwnerEmail = UniqueEmail("competition-other-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var otherOwnerId = await CreateUserAsync(factory, otherOwnerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga do Campeonato");
        await AddAssistantAsync(factory, organizationId, assistantId);
        await CreateOrganizationAsync(factory, otherOwnerId, "Liga Vizinha");

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var created = await owner.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "  Copa da Várzea  ", season = "2026", modality = "Fut7" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var competition = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/competitions/{competitionId}/settings", created.Headers.Location?.OriginalString);

        // Os opcionais assumem os padrões do produto (01 §9).
        Assert.Equal("Copa da Várzea", competition.GetProperty("name").GetString());
        Assert.Equal("Draft", competition.GetProperty("status").GetString());
        Assert.Equal("Owner", competition.GetProperty("viewerRole").GetString());
        Assert.Equal("Liga do Campeonato", competition.GetProperty("organizationName").GetString());
        Assert.Equal("America/Sao_Paulo", competition.GetProperty("timeZoneId").GetString());
        Assert.Equal(0, competition.GetProperty("marketCloseLeadTimeMinutes").GetInt32());
        Assert.Equal(2, competition.GetProperty("resultsSlaBusinessDays").GetInt32());
        Assert.Equal(3, competition.GetProperty("correctionWindowBusinessDays").GetInt32());
        Assert.True(competition.GetProperty("canChangeModality").GetBoolean());
        Assert.Equal(100m, competition.GetProperty("modalityProfile").GetProperty("budget").GetDecimal());
        Assert.False(string.IsNullOrEmpty(competition.GetProperty("version").GetString()));

        var ownerList = await owner.GetFromJsonAsync<JsonElement>(CompetitionsOf(organizationId), cancellationToken);
        Assert.Equal(competitionId, Assert.Single(ownerList.EnumerateArray()).GetProperty("id").GetGuid());

        // O auxiliar acompanha o campeonato, mas não cria nem altera.
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var assistantList = await assistant.GetFromJsonAsync<JsonElement>(
            CompetitionsOf(organizationId), cancellationToken);
        Assert.Single(assistantList.EnumerateArray());
        var assistantView = await assistant.GetFromJsonAsync<JsonElement>(
            SettingsOf(competitionId), cancellationToken);
        Assert.Equal("Assistant", assistantView.GetProperty("viewerRole").GetString());
        using var assistantCreate = await assistant.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "Copa do Auxiliar", season = "2026", modality = "Fut7" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantCreate.StatusCode);
        using var assistantUpdate = await assistant.PutAsJsonAsync(
            SettingsOf(competitionId), UpdateBody(competition, name: "Copa do Auxiliar"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantUpdate.StatusCode);

        // Outra organização não lê, não cria e não altera, nem trocando os IDs.
        using var otherOwner = await CreateAuthenticatedClientAsync(factory, otherOwnerEmail, cancellationToken);
        using var otherList = await otherOwner.GetAsync(CompetitionsOf(organizationId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherList.StatusCode);
        using var otherRead = await otherOwner.GetAsync(SettingsOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherRead.StatusCode);
        using var otherCreate = await otherOwner.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "Copa Invasora", season = "2026", modality = "Fut7" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherCreate.StatusCode);
        using var otherUpdate = await otherOwner.PutAsJsonAsync(
            SettingsOf(competitionId), UpdateBody(competition, name: "Copa Invasora"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherUpdate.StatusCode);

        // Campeonato inexistente responde igual a campeonato alheio.
        using var unknown = await owner.GetAsync(SettingsOf(Guid.NewGuid()), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);

        using var anonymous = factory.CreateClient();
        using var anonymousRead = await anonymous.GetAsync(SettingsOf(competitionId), cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal("Copa da Várzea", await dbContext.Competitions
            .Where(item => item.Id == competitionId)
            .Select(item => item.Name)
            .SingleAsync(cancellationToken));
        Assert.Equal(1, await dbContext.Competitions.CountAsync(
            item => item.OrganizationId == organizationId, cancellationToken));
        Assert.Contains("CompetitionDraftCreated", await dbContext.AdministrativeAuditEntries
            .Where(entry => entry.TargetId == competitionId)
            .Select(entry => entry.Action)
            .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task SettingsUpdateValidatesInputAndRefusesStaleVersion()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("settings-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga que Configura");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        using var invalidCreate = await owner.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "Co", season = "2026", modality = "Fut7", marketCloseLeadTimeMinutes = 5000 },
            cancellationToken);
        var invalidCreateErrors = await ValidationErrorsAsync(invalidCreate, cancellationToken);
        Assert.Contains("Name", invalidCreateErrors);
        Assert.Contains("MarketCloseLeadTimeMinutes", invalidCreateErrors);

        using var created = await owner.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "Copa Configurável", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var original = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var competitionId = original.GetProperty("id").GetGuid();

        // No rascunho a modalidade muda, e o perfil acompanha.
        using var updated = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            UpdateBody(original, name: "Copa de Campo", modality: "Field", marketCloseLeadTimeMinutes: 90),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var current = await updated.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("Field", current.GetProperty("modality").GetString());
        Assert.Equal(135m, current.GetProperty("modalityProfile").GetProperty("budget").GetDecimal());
        Assert.Equal(90, current.GetProperty("marketCloseLeadTimeMinutes").GetInt32());
        Assert.NotEqual(original.GetProperty("version").GetString(), current.GetProperty("version").GetString());

        // Quem editava sobre a leitura antiga recebe conflito e não sobrescreve.
        using var stale = await owner.PutAsJsonAsync(
            SettingsOf(competitionId), UpdateBody(original, name: "Copa Atrasada"), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var windowsTimeZone = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            UpdateBody(current, timeZoneId: "E. South America Standard Time"),
            cancellationToken);
        Assert.Contains("TimeZoneId", await ValidationErrorsAsync(windowsTimeZone, cancellationToken));

        using var numericModality = await owner.PutAsJsonAsync(
            SettingsOf(competitionId), UpdateBody(current, modality: "1"), cancellationToken);
        Assert.Contains("Modality", await ValidationErrorsAsync(numericModality, cancellationToken));

        using var withoutVersion = await owner.PutAsJsonAsync(
            SettingsOf(competitionId),
            new
            {
                name = "Copa sem Versão",
                season = "2026",
                modality = "Field",
                timeZoneId = "America/Sao_Paulo",
                marketCloseLeadTimeMinutes = 0,
                resultsSlaBusinessDays = 2,
                correctionWindowBusinessDays = 3,
            },
            cancellationToken);
        Assert.Contains("Version", await ValidationErrorsAsync(withoutVersion, cancellationToken));

        using var garbageVersion = await owner.PutAsJsonAsync(
            SettingsOf(competitionId), UpdateBody(current, version: "não é base64"), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, garbageVersion.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal("Copa de Campo", await dbContext.Competitions
            .Where(item => item.Id == competitionId)
            .Select(item => item.Name)
            .SingleAsync(cancellationToken));
        Assert.Equal(1, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.TargetId == competitionId && entry.Action == "CompetitionSettingsUpdated",
            cancellationToken));
    }

    [Fact]
    public async Task SimultaneousEditsFromTheSameVersionSaveOnlyOnce()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("concurrent-settings-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Concorrida");
        using var firstTab = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var secondTab = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);

        using var created = await firstTab.PostAsJsonAsync(
            CompetitionsOf(organizationId),
            new { name = "Copa Disputada", season = "2026", modality = "Futsal" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var original = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var competitionId = original.GetProperty("id").GetGuid();

        // Duas abas abertas sobre a mesma leitura salvam ao mesmo tempo, várias vezes.
        for (var round = 0; round < 5; round++)
        {
            var read = await firstTab.GetFromJsonAsync<JsonElement>(SettingsOf(competitionId), cancellationToken);
            var responses = await Task.WhenAll(
                firstTab.PutAsJsonAsync(
                    SettingsOf(competitionId), UpdateBody(read, name: $"Aba um {round}"), cancellationToken),
                secondTab.PutAsJsonAsync(
                    SettingsOf(competitionId), UpdateBody(read, name: $"Aba dois {round}"), cancellationToken));

            var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
            foreach (var response in responses)
            {
                response.Dispose();
            }

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(5, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.TargetId == competitionId && entry.Action == "CompetitionSettingsUpdated",
            cancellationToken));
    }

    private static Uri CompetitionsOf(Guid organizationId) =>
        new($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative);

    private static Uri SettingsOf(Guid competitionId) =>
        new($"/api/v1/competitions/{competitionId}/settings", UriKind.Relative);

    /// <summary>Corpo da edição a partir de uma leitura, trocando só o que o teste informa.</summary>
    private static object UpdateBody(
        JsonElement read,
        string? name = null,
        string? modality = null,
        string? timeZoneId = null,
        int? marketCloseLeadTimeMinutes = null,
        string? version = null) => new
        {
            name = name ?? read.GetProperty("name").GetString(),
            season = read.GetProperty("season").GetString(),
            modality = modality ?? read.GetProperty("modality").GetString(),
            timeZoneId = timeZoneId ?? read.GetProperty("timeZoneId").GetString(),
            marketCloseLeadTimeMinutes =
                marketCloseLeadTimeMinutes ?? read.GetProperty("marketCloseLeadTimeMinutes").GetInt32(),
            resultsSlaBusinessDays = read.GetProperty("resultsSlaBusinessDays").GetInt32(),
            correctionWindowBusinessDays = read.GetProperty("correctionWindowBusinessDays").GetInt32(),
            version = version ?? read.GetProperty("version").GetString(),
        };

    private static async Task<string[]> ValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }
}
