using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fut7Fantasy.Domain.Competitions;
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
/// Importação CSV de partidas (Fase 7): a tabela da temporada entra por arquivo, com as
/// mesmas regras do cadastro pela tela.
/// </summary>
public sealed class MatchImportFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task TemplateCreatesDraftRoundsAndResendingIsIdempotent()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi();
        var ownerEmail = UniqueEmail("match-import-owner");
        var assistantEmail = UniqueEmail("match-import-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Partidas");
        await AddAssistantAsync(factory, organizationId, assistantId);
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var competitionId = await CreateSeasonAsync(owner, organizationId, cancellationToken);

        // Os modelos de times e de partidas importam na ordem, sem editar nada.
        using var template = await owner.GetAsync(Template(competitionId, "partidas"), cancellationToken);
        template.EnsureSuccessStatusCode();
        var modelo = await template.Content.ReadAsByteArrayAsync(cancellationToken);

        var preview = await PostAsync(owner, competitionId, "partidas/preview", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal((3, 3, 0, 0), Summary(preview.Body));
        Assert.Contains("Rodada 1, Rodada 2", Assert.Single(Notes(preview.Body)), StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(factory, db => db.Rounds, competitionId, cancellationToken));

        var commit = await PostAsync(owner, competitionId, "partidas", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, commit.Status);
        Assert.Equal((3, 3, 0, 0), Summary(commit.Body));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
            var rounds = await dbContext.Rounds
                .Where(round => round.CompetitionId == competitionId)
                .OrderBy(round => round.Sequence)
                .ToListAsync(cancellationToken);
            Assert.Equal(["Rodada 1", "Rodada 2"], rounds.Select(round => round.Name));
            Assert.All(rounds, round => Assert.Equal(RoundStatus.Draft, round.Status));

            // 09:30 em Brasília é 12:30 em UTC: o fuso é o do campeonato.
            var first = await dbContext.Matches
                .Where(match => match.RoundId == rounds[0].Id)
                .OrderBy(match => match.KickoffAt)
                .FirstAsync(cancellationToken);
            Assert.Equal(new DateTimeOffset(2026, 9, 20, 12, 30, 0, TimeSpan.Zero), first.KickoffAt);
        }

        // Reenviar não duplica, e nenhuma rodada nova é anunciada.
        var again = await PostAsync(owner, competitionId, "partidas", modelo, cancellationToken);
        Assert.Equal((3, 0, 0, 3), Summary(again.Body));
        Assert.Empty(Notes(again.Body));
        Assert.Equal(3, await CountAsync(factory, db => db.Matches, competitionId, cancellationToken));

        // A identidade é rodada, mandante e visitante: outro horário remarca o jogo.
        var moved = await PostAsync(owner, competitionId, "partidas", Csv(
            """
            rodada;fase;mandante;visitante;data;hora
            Rodada 1;Primeira fase;União da Vila;Estrela do Bairro;20/09/2026;10:00
            """), cancellationToken);
        Assert.Equal((1, 0, 1, 0), Summary(moved.Body));
        Assert.Equal(3, await CountAsync(factory, db => db.Matches, competitionId, cancellationToken));

        // Marcar partidas é do proprietário, pelo arquivo como pela tela.
        var byAssistant = await PostAsync(assistant, competitionId, "partidas", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, byAssistant.Status);
    }

    [Fact]
    public async Task EveryScreenRuleIsReportedPerLineAndNothingIsWritten()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateApi();
        var ownerEmail = UniqueEmail("match-import-rules");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga das Regras");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateSeasonAsync(
            owner, organizationId, cancellationToken, splitGroups: true, extraTeam: "Time Solto");

        var invalid = await PostAsync(owner, competitionId, "partidas", Csv(
            """
            rodada;fase;mandante;visitante;data;hora
            Rodada 1;Primeira fase;União da Vila;Estrela do Bairro;20/09/2026;09:30
            Rodada 1;Primeira fase;União da Vila;Grêmio da Rua 9;20/09/2026;11:00
            Rodada 1;Primeira fase;União da Vila;Time Solto;20/09/2026;13:00
            Rodada 1;Fase Fantasma;Estrela do Bairro;União da Vila;20/09/2026;15:00
            Rodada 1;Primeira fase;Estrela do Bairro;Time Fantasma;20/09/2026;15:00
            Rodada 2;Primeira fase;União da Vila;União da Vila;27/09/2026;09:30
            Rodada 2;Primeira fase;Estrela do Bairro;União da Vila;31/02/2026;09:30
            Rodada 2;Primeira fase;Grêmio da Rua 9;Estrela do Bairro;27/09/2026;9h30
            """), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Equal("import_rows_invalid", Code(invalid.Body));
        Assert.Equal(
            [
                (3, "visitante"),
                (4, "visitante"),
                (5, "fase"),
                (6, "visitante"),
                (7, "visitante"),
                (8, "data"),
                (9, "hora"),
            ],
            Issues(invalid.Body).Select(issue => (
                issue.GetProperty("line").GetInt32(),
                issue.GetProperty("column").GetString()!)));

        // A linha boa também ficou de fora: ou entra o arquivo inteiro, ou nada.
        Assert.Equal(0, await CountAsync(factory, db => db.Rounds, competitionId, cancellationToken));
        Assert.Equal(0, await CountAsync(factory, db => db.Matches, competitionId, cancellationToken));

        var valid = await PostAsync(owner, competitionId, "partidas", Csv(
            """
            rodada;fase;mandante;visitante;data;hora
            Rodada 1;Primeira fase;União da Vila;Estrela do Bairro;20/09/2026;09:30
            """), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.Status);

        // Com o mercado aberto, a rodada só muda pela tela: remarcar, adiar ou cancelar.
        var rounds = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{competitionId}/rounds", cancellationToken);
        var round = rounds.EnumerateArray().Single();
        using var opened = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/rounds/{round.GetProperty("id").GetGuid()}/status",
            new { transition = "OpenMarket", version = round.GetProperty("version").GetString() },
            cancellationToken);
        opened.EnsureSuccessStatusCode();

        var locked = await PostAsync(owner, competitionId, "partidas", Csv(
            """
            rodada;fase;mandante;visitante;data;hora
            Rodada 1;Primeira fase;União da Vila;Estrela do Bairro;20/09/2026;10:00
            """), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, locked.Status);
        var issue = Assert.Single(Issues(locked.Body).ToList());
        Assert.Equal("rodada", issue.GetProperty("column").GetString());
        Assert.Contains("rascunho", issue.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>Relógio parado antes dos jogos do modelo, para que o mercado possa abrir.</summary>
    private WebApplicationFactory<Program> CreateApi() =>
        sqlServer.CreateApi(new CapturingEmailSender(), services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(ApiFactory.FixedNow));
        });

    /// <summary>
    /// Campeonato com os times do modelo e a fase "Primeira fase". Com
    /// <paramref name="splitGroups"/>, o Grêmio fica sozinho num segundo grupo.
    /// </summary>
    private static async Task<Guid> CreateSeasonAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken,
        bool splitGroups = false,
        string? extraTeam = null)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name = "Copa da Tabela", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var competitionId = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken))
            .GetProperty("id").GetGuid();

        using var teamsTemplate = await owner.GetAsync(Template(competitionId, "times"), cancellationToken);
        var teams = await PostAsync(
            owner,
            competitionId,
            "times",
            await teamsTemplate.Content.ReadAsByteArrayAsync(cancellationToken),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, teams.Status);
        if (extraTeam is not null)
        {
            using var extra = await owner.PostAsJsonAsync(
                $"/api/v1/competitions/{competitionId}/teams", new { name = extraTeam }, cancellationToken);
            extra.EnsureSuccessStatusCode();
        }

        var groups = splitGroups ? new[] { new { name = "A" }, new { name = "B" } } : [new { name = "A" }];
        using var stageResponse = await owner.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages",
            new { name = "Primeira fase", format = "Groups", groups },
            cancellationToken);
        stageResponse.EnsureSuccessStatusCode();
        var stage = await stageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var groupIds = stage.GetProperty("groups").EnumerateArray()
            .Select(group => group.GetProperty("id").GetGuid())
            .ToArray();

        var catalog = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/competitions/{competitionId}/teams", cancellationToken);
        var participants = catalog.EnumerateArray()
            .Where(team => team.GetProperty("name").GetString() != extraTeam)
            .Select(team => new
            {
                realTeamId = team.GetProperty("id").GetGuid(),
                stageGroupId = team.GetProperty("name").GetString() == "Grêmio da Rua 9" ? groupIds[^1] : groupIds[0],
            })
            .ToArray();
        using var confirmed = await owner.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/stages/{stage.GetProperty("id").GetGuid()}/participants",
            new { participants, version = stage.GetProperty("version").GetString() },
            cancellationToken);
        confirmed.EnsureSuccessStatusCode();
        return competitionId;
    }

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
}
