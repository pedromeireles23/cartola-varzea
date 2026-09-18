using System.Text;
using Fut7Fantasy.Api.Security;
using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Domain.Importing;
using Microsoft.AspNetCore.Http.Metadata;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>
/// Importação CSV do catálogo e das partidas: modelo, pré-visualização e confirmação.
///
/// O arquivo nunca é gravado em disco nem em blob. Ele é lido em memória, dentro do
/// limite de bytes, e descartado com a resposta (ADR-009).
///
/// O formulário é lido à mão, e não por parâmetro `IFormFile`, para que o middleware de
/// antiforgery do projeto continue sendo quem decide: nenhuma rota de escrita dispensa o
/// token, nem as de upload.
/// </summary>
public static class CatalogImportEndpoints
{
    /// <summary>Código estável de quem envia um arquivo que nem chega a ser lido.</summary>
    public const string RejectedCode = "import_file_rejected";

    /// <summary>Código estável de quando o arquivo é legível mas tem linhas inválidas.</summary>
    public const string InvalidCode = "import_rows_invalid";

    private const string FormField = "arquivo";

    public static IEndpointRouteBuilder MapCatalogImportEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/v1/import-templates", () =>
                Results.Ok(ImportTemplates.Current.Select(ImportTemplateView.From)))
            .AllowAnonymous()
            .WithTags("Importação")
            .WithName("GetImportTemplates")
            .WithSummary("Colunas, exemplos e versão de cada modelo de importação.");

        var imports = routes.MapGroup("/api/v1/competitions/{competitionId:guid}/imports")
            .WithTags("Importação");

        imports.MapGet("/{kind}/template", DownloadAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("DownloadImportTemplate")
            .WithSummary("Baixa o arquivo modelo, com linhas de exemplo fictícias.");
        imports.MapPost("/{kind}/preview", PreviewAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("PreviewCatalogImport")
            .WithSummary("Lê o arquivo e mostra o que aconteceria, sem gravar nada.")
            .WithMetadata(new RequestSizeLimitMetadata());
        imports.MapPost("/{kind}", CommitAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionOwnerWrite)
            .WithName("CommitCatalogImport")
            .WithSummary("Revalida o arquivo e grava tudo, ou nada, em transação única.")
            .WithMetadata(new RequestSizeLimitMetadata());

        // Estatísticas são da rodada, e quem lança súmula também importa: proprietário e
        // auxiliar, como no editor da partida.
        var statistics = routes
            .MapGroup("/api/v1/competitions/{competitionId:guid}/rounds/{roundId:guid}/imports/estatisticas")
            .WithTags("Importação");

        statistics.MapGet("/template", DownloadStatisticsAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionMember)
            .WithName("DownloadRoundStatisticsTemplate")
            .WithSummary("Baixa o modelo preenchido com os jogos e os elencos da rodada.");
        statistics.MapPost("/preview", PreviewStatisticsAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionStaffWrite)
            .WithName("PreviewRoundStatisticsImport")
            .WithSummary("Lê o arquivo e mostra placares e súmulas que mudariam, sem gravar nada.")
            .WithMetadata(new RequestSizeLimitMetadata());
        statistics.MapPost("/", CommitStatisticsAsync)
            .RequireAuthorization(AuthorizationPolicies.CompetitionStaffWrite)
            .WithName("CommitRoundStatisticsImport")
            .WithSummary("Revalida o arquivo e grava todas as súmulas, ou nenhuma.")
            .WithMetadata(new RequestSizeLimitMetadata());

        return routes;
    }

    private static IResult DownloadAsync(string kind)
    {
        if (!TryParseKind(kind, out var parsed))
        {
            return Results.NotFound();
        }

        var template = ImportTemplates.For(parsed);
        return CsvFile(template.Render(), template.FileName);
    }

    private static async Task<IResult> DownloadStatisticsAsync(
        Guid competitionId,
        Guid roundId,
        IRoundStatisticsImportService service,
        CancellationToken cancellationToken) =>
        await service.TemplateAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false) is { } file
            ? CsvFile(file.Content, file.FileName)
            : Results.NotFound();

    /// <summary>BOM porque o Excel no Windows assume ANSI sem ele e estraga os acentos.</summary>
    private static IResult CsvFile(string content, string fileName)
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(content))
            .ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static async Task<IResult> PreviewStatisticsAsync(
        Guid competitionId,
        Guid roundId,
        HttpRequest request,
        IRoundStatisticsImportService service,
        CancellationToken cancellationToken)
    {
        var (content, rejected) = await ReadFileAsync(request, cancellationToken).ConfigureAwait(false);
        return rejected ?? Respond(
            await service.PreviewAsync(competitionId, roundId, content, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> CommitStatisticsAsync(
        Guid competitionId,
        Guid roundId,
        HttpRequest request,
        IRoundStatisticsImportService service,
        CancellationToken cancellationToken)
    {
        var (content, rejected) = await ReadFileAsync(request, cancellationToken).ConfigureAwait(false);
        return rejected ?? Respond(
            await service.CommitAsync(competitionId, roundId, content, cancellationToken).ConfigureAwait(false));
    }

    private static Task<IResult> PreviewAsync(
        Guid competitionId,
        string kind,
        HttpRequest request,
        ICatalogImportService service,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, kind, request, service, apply: false, cancellationToken);

    private static Task<IResult> CommitAsync(
        Guid competitionId,
        string kind,
        HttpRequest request,
        ICatalogImportService service,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, kind, request, service, apply: true, cancellationToken);

    private static async Task<IResult> RunAsync(
        Guid competitionId,
        string kind,
        HttpRequest request,
        ICatalogImportService service,
        bool apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryParseKind(kind, out var parsed))
        {
            return Results.NotFound();
        }

        var (content, rejected) = await ReadFileAsync(request, cancellationToken).ConfigureAwait(false);
        if (rejected is not null)
        {
            return rejected;
        }

        return Respond(apply
            ? await service.CommitAsync(competitionId, parsed, content, cancellationToken).ConfigureAwait(false)
            : await service.PreviewAsync(competitionId, parsed, content, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Lê o arquivo do formulário para a memória, dentro do limite. Devolve a recusa
    /// pronta quando nem há arquivo para ler.
    /// </summary>
    private static async Task<(byte[] Content, IResult? Rejected)> ReadFileAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasFormContentType)
        {
            return ([], Rejected($"Envie o arquivo no campo `{FormField}` de um formulário."));
        }

        var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files[FormField];
        if (file is null || file.Length == 0)
        {
            return ([], Rejected("Escolha um arquivo CSV."));
        }

        if (file.Length > CsvLimits.MaxBytes)
        {
            return ([], Rejected($"O arquivo passa de {CsvLimits.MaxBytes / 1024} KB. Divida a planilha em partes."));
        }

        // O nome do arquivo enviado nunca vira caminho nem nome interno (04 §9).
        using var buffer = new MemoryStream((int)file.Length);
        await using (var stream = file.OpenReadStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), null);
    }

    private static IResult Respond(ImportResult result) =>
        result.Outcome switch
        {
            ImportOutcome.Completed => Results.Ok(result),
            ImportOutcome.NotFound => Results.NotFound(),
            ImportOutcome.Rejected => Rejected(result.RejectionMessage ?? "Não foi possível ler o arquivo."),
            _ => Results.Problem(
                title: "Arquivo com linhas inválidas",
                detail: "Nada foi gravado. Corrija as linhas indicadas e envie de novo.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = InvalidCode,
                    ["issues"] = result.Issues,
                }),
        };

    private static IResult Rejected(string detail) => Results.Problem(
        title: "Arquivo recusado",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["code"] = RejectedCode });

    /// <summary>As rotas usam o nome em português, como o resto da interface.</summary>
    private static bool TryParseKind(string kind, out ImportKind parsed)
    {
        parsed = kind switch
        {
            "times" => ImportKind.Teams,
            "atletas" => ImportKind.Athletes,
            "tecnicos" => ImportKind.Coaches,
            "partidas" => ImportKind.Matches,
            _ => default,
        };
        return parsed != default;
    }
}

/// <summary>
/// Teto de bytes do corpo, aplicado pelo servidor antes de qualquer leitura. O limite do
/// leitor CSV continua valendo: este só evita que o processo receba o arquivo inteiro.
/// </summary>
internal sealed class RequestSizeLimitMetadata : IRequestSizeLimitMetadata
{
    /// <summary>Folga sobre o limite do CSV para caber o envelope do multipart.</summary>
    public long? MaxRequestBodySize => CsvLimits.MaxBytes + (64 * 1024);
}
