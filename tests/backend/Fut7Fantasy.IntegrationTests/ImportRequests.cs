using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>Envio de arquivo e leitura da resposta das importações CSV.</summary>
internal static class ImportRequests
{
    public static Uri Template(Guid competitionId, string kind) =>
        new($"/api/v1/competitions/{competitionId}/imports/{kind}/template", UriKind.Relative);

    /// <summary>Lê um arquivo de `infra/dados-demo` a partir da raiz do repositório.</summary>
    public static byte[] DemoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "infra")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllBytes(Path.Combine(directory.FullName, "infra", "dados-demo", fileName));
    }

    public static byte[] Csv(string content) =>
        Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\r\n"));

    /// <summary>Envia para `/competitions/{id}/imports/{path}`, a importação do campeonato.</summary>
    public static Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(
        HttpClient client,
        Guid competitionId,
        string path,
        byte[] content,
        CancellationToken cancellationToken) =>
        PostFileAsync(
            client,
            new Uri($"/api/v1/competitions/{competitionId}/imports/{path}", UriKind.Relative),
            content,
            cancellationToken);

    public static async Task<(HttpStatusCode Status, JsonElement Body)> PostFileAsync(
        HttpClient client,
        Uri uri,
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "arquivo", "catalogo.csv");
        using var response = await client.PostAsync(uri, form, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return (response.StatusCode, default);
        }

        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
    }

    public static (int Rows, int Created, int Updated, int Unchanged) Summary(JsonElement body)
    {
        var summary = body.GetProperty("summary");
        return (
            summary.GetProperty("rows").GetInt32(),
            summary.GetProperty("created").GetInt32(),
            summary.GetProperty("updated").GetInt32(),
            summary.GetProperty("unchanged").GetInt32());
    }

    public static string[] Notes(JsonElement body) =>
        [.. body.GetProperty("notes").EnumerateArray().Select(note => note.GetString()!)];

    public static string? Code(JsonElement body) => body.GetProperty("code").GetString();

    public static JsonElement.ArrayEnumerator Issues(JsonElement body) =>
        body.GetProperty("issues").EnumerateArray();
}
