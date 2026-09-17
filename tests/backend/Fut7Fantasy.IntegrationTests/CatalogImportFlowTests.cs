using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Fut7Fantasy.IntegrationTests.TestAccounts;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// Importação CSV do catálogo: modelo, pré-visualização sem escrita, confirmação
/// transacional e reenvio idempotente (Fase 6).
/// </summary>
public sealed class CatalogImportFlowTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task PreviewDoesNotWriteAndCommitAppliesEverythingOnce()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("import-owner");
        var assistantEmail = UniqueEmail("import-assistant");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var assistantId = await CreateUserAsync(factory, assistantEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Importada");
        await AddAssistantAsync(factory, organizationId, assistantId);

        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        using var assistant = await CreateAuthenticatedClientAsync(factory, assistantEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        // O modelo baixado é o próprio arquivo de exemplo, e ele importa sem edição.
        using var template = await owner.GetAsync(Template(competitionId, "times"), cancellationToken);
        template.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", template.Content.Headers.ContentType?.MediaType);
        var modelo = await template.Content.ReadAsByteArrayAsync(cancellationToken);

        var preview = await PostAsync(owner, competitionId, "times/preview", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal((3, 3, 0, 0), Summary(preview.Body));

        // Pré-visualizar não grava: o catálogo continua vazio.
        Assert.Equal(0, await CountTeamsAsync(factory, competitionId, cancellationToken));

        var commit = await PostAsync(owner, competitionId, "times", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, commit.Status);
        Assert.Equal((3, 3, 0, 0), Summary(commit.Body));
        Assert.Equal(3, await CountTeamsAsync(factory, competitionId, cancellationToken));

        // Reenviar o mesmo arquivo não duplica nada e diz isso com todas as letras.
        var again = await PostAsync(owner, competitionId, "times", modelo, cancellationToken);
        Assert.Equal((3, 0, 0, 3), Summary(again.Body));
        Assert.Equal(3, await CountTeamsAsync(factory, competitionId, cancellationToken));

        // Cada time importado já nasce com o ativo de técnico, como no cadastro manual.
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        Assert.Equal(3, await dbContext.Coaches.CountAsync(
            coach => coach.CompetitionId == competitionId, cancellationToken));
        Assert.Equal(2, await dbContext.AdministrativeAuditEntries.CountAsync(
            entry => entry.Action == "CatalogImportedTeams" && entry.TargetId == competitionId,
            cancellationToken));

        // Auxiliar baixa o modelo, mas não importa.
        using var assistantTemplate = await assistant.GetAsync(
            Template(competitionId, "times"), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, assistantTemplate.StatusCode);
        var assistantImport = await PostAsync(assistant, competitionId, "times", modelo, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, assistantImport.Status);
    }

    [Fact]
    public async Task AthletesAndCoachesFollowTheTeamsAndNothingIsWrittenWhenALineFails()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("import-catalog-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga do Catálogo");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var teams = await PostAsync(owner, competitionId, "times", Csv(
            """
            nome
            União da Vila
            Estrela do Bairro
            """), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, teams.Status);

        // Atleta que aponta para um time inexistente para o arquivo inteiro.
        var orphan = await PostAsync(owner, competitionId, "atletas", Csv(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;meio-campista;destaque;
            Nena;Time Fantasma;goleiro;;
            """), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, orphan.Status);
        Assert.Equal("import_rows_invalid", Code(orphan.Body));
        Assert.Equal([3], Issues(orphan.Body).Select(issue => issue.GetProperty("line").GetInt32()));
        Assert.Contains("Time Fantasma", Issues(orphan.Body).First().GetProperty("message").GetString()!);

        // Nem a linha boa foi gravada: ou entra tudo, ou nada.
        Assert.Equal(0, await CountAthletesAsync(factory, competitionId, cancellationToken));

        var athletes = await PostAsync(owner, competitionId, "atletas", Csv(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;meio-campista;destaque;
            Nena;União da Vila;goleiro;;
            Tatá;Estrela do Bairro;atacante;basico;6,5
            """), cancellationToken);
        Assert.Equal((3, 3, 0, 0), Summary(athletes.Body));

        // O mesmo arquivo com um preço diferente altera só a linha que mudou.
        var changed = await PostAsync(owner, competitionId, "atletas", Csv(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;meio-campista;destaque;
            Nena;União da Vila;goleiro;;
            Tatá;Estrela do Bairro;atacante;basico;7
            """), cancellationToken);
        Assert.Equal((3, 0, 1, 2), Summary(changed.Body));

        // Transferência entre times continua recusada, venha da tela ou do arquivo.
        var transfer = await PostAsync(owner, competitionId, "atletas", Csv(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;Estrela do Bairro;meio-campista;destaque;
            """), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, transfer.Status);
        Assert.Contains(
            "não é transferido",
            Issues(transfer.Body).First().GetProperty("message").GetString()!,
            StringComparison.Ordinal);

        // O técnico já existe desde a criação do time: a importação só o altera.
        var coaches = await PostAsync(owner, competitionId, "tecnicos", Csv(
            """
            time;nome;nivel_preco;preco_exato
            União da Vila;Seu Zé;destaque;
            Estrela do Bairro;;;
            """), cancellationToken);
        Assert.Equal((2, 0, 1, 1), Summary(coaches.Body));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        var named = await dbContext.Coaches
            .Where(coach => coach.CompetitionId == competitionId && coach.DisplayName != null)
            .Select(coach => coach.DisplayName)
            .ToListAsync(cancellationToken);
        Assert.Equal(["Seu Zé"], named);
    }

    [Fact]
    public async Task UnreadableFilesAreRejectedWithAnExplanationInsteadOfAStackTrace()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("import-reject-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Recusada");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        var oversized = await PostAsync(
            owner, competitionId, "times", new byte[(1024 * 1024) + 1], cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.Status);
        Assert.Equal("import_file_rejected", Code(oversized.Body));

        // Planilha salva em ANSI: recusar é melhor que importar acento quebrado.
        var latin1 = Encoding.Latin1.GetBytes("nome\r\nUnião da Vila\r\n");
        var encoding = await PostAsync(owner, competitionId, "times", latin1, cancellationToken);
        Assert.Equal("import_file_rejected", Code(encoding.Body));
        Assert.Contains("UTF-8", encoding.Body.GetProperty("detail").GetString()!, StringComparison.Ordinal);

        var empty = await PostAsync(owner, competitionId, "times", Csv("\r\n"), cancellationToken);
        Assert.Equal("import_file_rejected", Code(empty.Body));

        // Modelo errado no lugar certo: o cabeçalho denuncia antes de qualquer linha.
        var wrongTemplate = await PostAsync(owner, competitionId, "times", Csv(
            """
            nome_esportivo;time;posicao
            Bia;União da Vila;meio-campista
            """), cancellationToken);
        Assert.Equal("import_rows_invalid", Code(wrongTemplate.Body));
        Assert.Equal([1], Issues(wrongTemplate.Body).Select(issue => issue.GetProperty("line").GetInt32()).Distinct());

        Assert.Equal(0, await CountTeamsAsync(factory, competitionId, cancellationToken));

        using var unknownKind = await owner.PostAsync(
            new Uri($"/api/v1/competitions/{competitionId}/imports/partidas", UriKind.Relative),
            Multipart(Csv("nome\r\n")),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownKind.StatusCode);
    }

    [Fact]
    public async Task DemoDataFilesImportAndLeaveTheCompetitionReadyToPublish()
    {
        Assert.SkipWhen(sqlServer.Unavailable is not null, sqlServer.Unavailable ?? string.Empty);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = sqlServer.CreateApi();
        var ownerEmail = UniqueEmail("import-demo-owner");
        var ownerId = await CreateUserAsync(factory, ownerEmail);
        var organizationId = await CreateOrganizationAsync(factory, ownerId, "Liga Demonstração");
        using var owner = await CreateAuthenticatedClientAsync(factory, ownerEmail, cancellationToken);
        var competitionId = await CreateCompetitionAsync(owner, organizationId, cancellationToken);

        // Os arquivos de `infra/dados-demo` são o roteiro da demonstração: se um deles
        // parar de importar, a demonstração quebra antes de alguém perceber.
        foreach (var (kind, file) in new[]
        {
            ("times", "times-v1.csv"),
            ("atletas", "atletas-v1.csv"),
            ("tecnicos", "tecnicos-v1.csv"),
        })
        {
            var result = await PostAsync(owner, competitionId, kind, DemoFile(file), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal(0, Summary(result.Body).Unchanged);
        }

        Assert.Equal(6, await CountTeamsAsync(factory, competitionId, cancellationToken));
        Assert.Equal(54, await CountAthletesAsync(factory, competitionId, cancellationToken));

        // Só falta a fase com times: o catálogo em si já passa no checklist.
        var readiness = await owner.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/competitions/{competitionId}/readiness", UriKind.Relative), cancellationToken);
        var blockers = readiness.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("severity").GetString() == "Blocker")
            .Select(item => item.GetProperty("code").GetString())
            .ToArray();
        Assert.Equal(["no_stages"], blockers);

        // Elenco de 9 por time é o mínimo do Fut7, então nem alerta de elenco curto sobra.
        Assert.DoesNotContain(
            "thin_real_team_roster",
            readiness.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("code").GetString()));
    }

    /// <summary>Lê um arquivo de `infra/dados-demo` a partir da raiz do repositório.</summary>
    private static byte[] DemoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "infra")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllBytes(Path.Combine(directory.FullName, "infra", "dados-demo", fileName));
    }

    private static Uri Template(Guid competitionId, string kind) =>
        new($"/api/v1/competitions/{competitionId}/imports/{kind}/template", UriKind.Relative);

    private static byte[] Csv(string content) =>
        Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\r\n"));

    private static MultipartFormDataContent Multipart(byte[] content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "arquivo", "catalogo.csv");
        return form;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(
        HttpClient client,
        Guid competitionId,
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var form = Multipart(content);
        using var response = await client.PostAsync(
            new Uri($"/api/v1/competitions/{competitionId}/imports/{path}", UriKind.Relative),
            form,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (response.StatusCode, default);
        }

        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    private static (int Rows, int Created, int Updated, int Unchanged) Summary(JsonElement body)
    {
        var summary = body.GetProperty("summary");
        return (
            summary.GetProperty("rows").GetInt32(),
            summary.GetProperty("created").GetInt32(),
            summary.GetProperty("updated").GetInt32(),
            summary.GetProperty("unchanged").GetInt32());
    }

    private static string? Code(JsonElement body) => body.GetProperty("code").GetString();

    private static JsonElement.ArrayEnumerator Issues(JsonElement body) =>
        body.GetProperty("issues").EnumerateArray();

    private static async Task<int> CountTeamsAsync(
        WebApplicationFactory<Program> factory,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        return await dbContext.RealTeams.CountAsync(
            team => team.CompetitionId == competitionId, cancellationToken);
    }

    private static async Task<int> CountAthletesAsync(
        WebApplicationFactory<Program> factory,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Fut7FantasyDbContext>();
        return await dbContext.Athletes.CountAsync(
            athlete => athlete.CompetitionId == competitionId, cancellationToken);
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient owner,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var created = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/competitions", UriKind.Relative),
            new { name = "Copa Importada", season = "2026", modality = "Fut7" },
            cancellationToken);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("id").GetGuid();
    }
}
