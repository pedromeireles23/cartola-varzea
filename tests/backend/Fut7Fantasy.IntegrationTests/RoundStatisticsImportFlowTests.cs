using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using static Fut7Fantasy.IntegrationTests.ImportRequests;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Estatísticas de uma rodada por CSV (Fase 7): o modelo vem preenchido com jogos e
/// elencos, o placar sai dos gols e a súmula existente é substituída com aviso.
/// </summary>
public sealed class RoundStatisticsImportFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string Header =
        "mandante;visitante;atleta;time;jogou;goleiro;gols_sofridos;gols;assistencias;defesas;"
        + "penaltis_defendidos;amarelos;vermelho;gols_contra;penaltis_perdidos";

    [Fact]
    public async Task AssistantImportsTheRoundFromThePrefilledTemplateAndResendingIsIdempotent()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var ownerEmail = UniqueEmail("stats-owner");
        var assistantEmail = UniqueEmail("stats-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Estatísticas");
        await AddAssistantAsync(factory, organizationId, assistantId);
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken);
        clock.Advance(TimeSpan.FromHours(3));

        // O modelo já traz o jogo e o elenco elegível, mandante primeiro.
        using var template = await assistant.GetAsync(TemplateUri(world), cancellationToken);
        template.EnsureSuccessStatusCode();
        Assert.Equal("estatisticas-v1-rodada-1.csv", template.Content.Headers.ContentDisposition?.FileNameStar);
        var lines = await LinesAsync(template, cancellationToken);
        Assert.Equal(Header, lines[0]);
        Assert.Equal(
            ["Ana", "Bia", "Cris", "Dani"],
            lines.Skip(1).Select(line => line.Split(';')[2]));
        Assert.All(lines.Skip(1), line => Assert.Equal("nao", line.Split(';')[4]));

        // Gols sofridos em branco: com um goleiro só, o servidor usa o placar adversário.
        var file = Csv(string.Join(
            "\n",
            Header,
            Row("Ana", "Aurora", goalkeeper: true),
            Row("Bia", "Aurora", goals: 2, yellows: 1),
            Row("Cris", "Estrela", goalkeeper: true),
            Row("Dani", "Estrela", goals: 1)));

        var preview = await PostFileAsync(assistant, ImportUri(world, preview: true), file, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal((4, 1, 0, 0), Summary(preview.Body));
        Assert.Equal(["Aurora 2 x 1 Estrela: súmula nova."], Notes(preview.Body));
        Assert.Equal(0, await CountAsync(factory, db => db.MatchSheets, world.CompetitionId, cancellationToken));

        var commit = await PostFileAsync(assistant, ImportUri(world, preview: false), file, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, commit.Status);
        Assert.Equal((4, 1, 0, 0), Summary(commit.Body));

        var sheet = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{world.CompetitionId}/matches/{world.MatchId}/sheet", cancellationToken);
        Assert.Equal((2, 1), (sheet.GetProperty("homeScore").GetInt32(), sheet.GetProperty("awayScore").GetInt32()));
        var conceded = sheet.GetProperty("athletes").EnumerateArray()
            .ToDictionary(
                athlete => athlete.GetProperty("sportingName").GetString()!,
                athlete => athlete.GetProperty("goalsConceded").GetInt32());
        Assert.Equal(1, conceded["Ana"]);
        Assert.Equal(2, conceded["Cris"]);

        // Baixar de novo traz o que foi lançado: é assim que se corrige pela planilha.
        using var filled = await assistant.GetAsync(TemplateUri(world), cancellationToken);
        var bia = (await LinesAsync(filled, cancellationToken)).Single(line => line.Split(';')[2] == "Bia").Split(';');
        Assert.Equal(("sim", "2", "1"), (bia[4], bia[7], bia[11]));

        // Reenviar o mesmo arquivo não muda nada, e a prévia diz isso.
        var again = await PostFileAsync(assistant, ImportUri(world, preview: false), file, cancellationToken);
        Assert.Equal((4, 0, 0, 1), Summary(again.Body));
        Assert.Equal(["Aurora 2 x 1 Estrela: igual à súmula lançada."], Notes(again.Body));

        // Um gol a menos muda o placar e substitui a súmula, com aviso antes.
        var corrected = Csv(string.Join(
            "\n",
            Header,
            Row("Ana", "Aurora", goalkeeper: true),
            Row("Bia", "Aurora", goals: 1, yellows: 1),
            Row("Cris", "Estrela", goalkeeper: true),
            Row("Dani", "Estrela", goals: 1)));
        var replacing = await PostFileAsync(assistant, ImportUri(world, preview: true), corrected, cancellationToken);
        Assert.Equal((4, 0, 1, 0), Summary(replacing.Body));
        Assert.Equal(["Aurora 1 x 1 Estrela: substitui a súmula já lançada."], Notes(replacing.Body));
        var replaced = await PostFileAsync(assistant, ImportUri(world, preview: false), corrected, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, replaced.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var stored = await dbContext.MatchSheets.SingleAsync(
            item => item.MatchId == world.MatchId, cancellationToken);
        Assert.Equal((1, 1), (stored.HomeScore, stored.AwayScore));
        Assert.Equal(2, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CompetitionMatchSheetImported" && entry.TargetId == stored.Id,
            cancellationToken));
        Assert.Equal(3, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "RoundStatisticsImported" && entry.TargetId == world.RoundId,
            cancellationToken));
    }

    [Fact]
    public async Task EveryProblemPointsAtItsLineAndNothingIsWritten()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(ApiFactory.FixedNow);
        using var factory = CreateApi(clock);
        var ownerEmail = UniqueEmail("stats-rules");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Regras da Súmula");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var world = await BuildAsync(owner, organizationId, cancellationToken);

        var valid = Csv(string.Join(
            "\n",
            Header,
            Row("Ana", "Aurora", goalkeeper: true),
            Row("Cris", "Estrela", goalkeeper: true)));

        // Antes do jogo a rodada ainda está com o mercado aberto e não recebe súmula.
        var early = await PostFileAsync(owner, ImportUri(world, preview: true), valid, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, early.Status);
        Assert.Contains(
            "não recebe súmula agora",
            Assert.Single(Issues(early.Body).ToList()).GetProperty("message").GetString()!,
            StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromHours(3));
        var invalid = await PostFileAsync(owner, ImportUri(world, preview: false), Csv(string.Join(
            "\n",
            Header,
            Row("Bia", "", played: "nao", goals: 1),
            Row("Zé", ""),
            Row("Ana", "Estrela", goalkeeper: true),
            Row("Dani", "", yellows: 1, red: "segundo amarelo"),
            Row("Cris", "", home: "Estrela", away: "Aurora"),
            Row("Cris", "", played: "talvez"))), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Equal("import_rows_invalid", Code(invalid.Body));
        Assert.Equal(
            [(2, "jogou"), (3, "atleta"), (4, "time"), (5, "amarelos"), (6, "mandante"), (7, "jogou")],
            Issues(invalid.Body).Select(issue => (
                issue.GetProperty("line").GetInt32(),
                issue.GetProperty("column").GetString()!)));

        // Regra do jogo inteiro: sem goleiro no Estrela, o erro aponta a primeira linha do jogo.
        var withoutGoalkeeper = await PostFileAsync(owner, ImportUri(world, preview: false), Csv(string.Join(
            "\n",
            Header,
            Row("Ana", "Aurora", goalkeeper: true),
            Row("Dani", "Estrela"))), cancellationToken);
        var issue = Assert.Single(Issues(withoutGoalkeeper.Body).ToList());
        Assert.Equal(2, issue.GetProperty("line").GetInt32());
        Assert.StartsWith("Aurora x Estrela:", issue.GetProperty("message").GetString()!, StringComparison.Ordinal);

        Assert.Equal(0, await CountAsync(factory, db => db.MatchSheets, world.CompetitionId, cancellationToken));

        // Quem só visualiza baixa o modelo, mas não importa.
        var viewerEmail = UniqueEmail("stats-viewer");
        await CreateUserAsync(factory, viewerEmail, "DemoViewer");
        using var viewer = await CreateAuthenticatedClientAsync(factory, viewerEmail, cancellationToken);
        var denied = await PostFileAsync(viewer, ImportUri(world, preview: false), valid, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
    }

    /// <summary>Uma linha do arquivo, com zeros onde o teste não diz nada.</summary>
    private static string Row(
        string athlete,
        string team,
        string home = "Aurora",
        string away = "Estrela",
        string played = "sim",
        bool goalkeeper = false,
        int goals = 0,
        int yellows = 0,
        string red = "") =>
        string.Join(
            ';',
            home,
            away,
            athlete,
            team,
            played,
            goalkeeper ? "sim" : "nao",
            string.Empty,
            goals.ToString(CultureInfo.InvariantCulture),
            "0",
            "0",
            "0",
            yellows.ToString(CultureInfo.InvariantCulture),
            red,
            "0",
            "0");

    private static async Task<string[]> LinesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new UTF8Encoding(false).GetString(bytes)
            .TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static Uri TemplateUri(World world) => new(
        $"/api/v1/competitions/{world.CompetitionId}/rounds/{world.RoundId}/imports/estatisticas/template",
        UriKind.Relative);

    private static Uri ImportUri(World world, bool preview) => new(
        $"/api/v1/competitions/{world.CompetitionId}/rounds/{world.RoundId}/imports/estatisticas"
        + (preview ? "/preview" : string.Empty),
        UriKind.Relative);

    private WebApplicationFactory<Program> CreateApi(FakeTimeProvider clock) =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

    /// <summary>Aurora x Estrela daqui a duas horas, com um goleiro e um atacante de cada lado.</summary>
    private static async Task<World> BuildAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var competitionResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId}/competitions",
            new { name = "Copa das Planilhas", season = "2026", modality = "Fut7", marketCloseLeadTimeMinutes = 60 },
            cancellationToken);
        competitionResponse.EnsureSuccessStatusCode();
        var competitionId = Id(await competitionResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
        var home = await PostIdAsync(
            owner, $"/api/v1/competitions/{competitionId}/teams", new { name = "Aurora" }, cancellationToken);
        var away = await PostIdAsync(
            owner, $"/api/v1/competitions/{competitionId}/teams", new { name = "Estrela" }, cancellationToken);

        using var stageResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages",
            new { name = "Grupo único", format = "Groups", groups = new[] { new { name = "Grupo A" } } },
            cancellationToken);
        stageResponse.EnsureSuccessStatusCode();
        var stage = await stageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var groupId = stage.GetProperty("groups")[0].GetProperty("id").GetGuid();
        using var participants = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages/{Id(stage)}/participants",
            new
            {
                participants = new[]
                {
                    new { realTeamId = home, stageGroupId = groupId },
                    new { realTeamId = away, stageGroupId = groupId },
                },
                version = stage.GetProperty("version").GetString(),
            },
            cancellationToken);
        participants.EnsureSuccessStatusCode();

        foreach (var (name, team, position) in new[]
        {
            ("Ana", home, "Goalkeeper"),
            ("Bia", home, "Forward"),
            ("Cris", away, "Goalkeeper"),
            ("Dani", away, "Forward"),
        })
        {
            await PostIdAsync(
                owner,
                $"/api/v1/competitions/{competitionId}/athletes",
                new { sportingName = name, position, realTeamId = team, priceTier = "Regular" },
                cancellationToken);
        }

        using var roundResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds", new { name = "Rodada 1" }, cancellationToken);
        roundResponse.EnsureSuccessStatusCode();
        var round = await roundResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var kickoffLocal = ApiFactory.FixedNow.AddHours(2).ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        using var matchResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/matches",
            new
            {
                stageId = Id(stage),
                homeTeamId = home,
                awayTeamId = away,
                kickoffLocal,
                version = round.GetProperty("version").GetString(),
            },
            cancellationToken);
        matchResponse.EnsureSuccessStatusCode();
        var withMatch = await matchResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var opened = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{Id(round)}/status",
            new { transition = "OpenMarket", version = withMatch.GetProperty("version").GetString() },
            cancellationToken);
        opened.EnsureSuccessStatusCode();
        return new(competitionId, Id(round), withMatch.GetProperty("matches")[0].GetProperty("id").GetGuid());
    }

    private static async Task<Guid> PostIdAsync(
        HttpClient client,
        string uri,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(uri, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Id(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static async Task<int> CountAsync<T>(
        WebApplicationFactory<Program> factory,
        Func<Fut7FantasyDbContext, IQueryable<T>> set,
        Guid competitionId,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        return await set(dbContext).CountAsync(
            item => EF.Property<Guid>(item, "CompetitionId") == competitionId, cancellationToken);
    }

    private sealed record World(Guid CompetitionId, Guid RoundId, Guid MatchId);
}
