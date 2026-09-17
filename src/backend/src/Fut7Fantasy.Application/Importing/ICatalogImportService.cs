using Fut7Fantasy.Domain.Importing;

namespace Fut7Fantasy.Application.Importing;

/// <summary>
/// Importação CSV do catálogo esportivo (03 §13, ADR-009).
///
/// A pré-visualização não grava nada; a confirmação relê o mesmo arquivo do zero e grava
/// em transação única. Não existe rascunho da importação guardado no servidor: quem
/// segura o arquivo entre uma etapa e outra é o navegador.
/// </summary>
public interface ICatalogImportService
{
    /// <summary>Lê e valida sem gravar, devolvendo o que aconteceria.</summary>
    Task<ImportResult> PreviewAsync(
        Guid competitionId,
        ImportKind kind,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revalida e grava. Se qualquer linha falhar, nada é gravado: o organizador corrige
    /// a planilha e reenvia, em vez de descobrir metade do catálogo importado.
    /// </summary>
    Task<ImportResult> CommitAsync(
        Guid competitionId,
        ImportKind kind,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

public enum ImportOutcome
{
    /// <summary>Arquivo válido: na pré-visualização nada foi gravado; na confirmação, tudo.</summary>
    Completed,

    /// <summary>Há erro em alguma linha ou no cabeçalho; nada foi gravado.</summary>
    Invalid,

    /// <summary>O arquivo inteiro foi recusado antes de olhar conteúdo (tamanho, encoding…).</summary>
    Rejected,

    NotFound,
}

/// <summary>
/// O que a importação faria, ou fez. As contagens separam o que muda do que já estava
/// igual, para que reenviar o mesmo arquivo mostre claramente que nada mudou.
/// </summary>
/// <param name="Rows">Linhas de dados válidas lidas do arquivo.</param>
/// <param name="Created">Registros que passam a existir.</param>
/// <param name="Updated">Registros que existem e mudam de conteúdo.</param>
/// <param name="Unchanged">Registros idênticos aos que já estão no catálogo.</param>
public sealed record ImportSummary(int Rows, int Created, int Updated, int Unchanged);

public sealed record ImportIssueView(int Line, string Column, string Message);

public sealed record ImportResult(
    ImportOutcome Outcome,
    ImportSummary Summary,
    IReadOnlyList<ImportIssueView> Issues,
    string? RejectionMessage)
{
    public static ImportResult Of(ImportOutcome outcome) =>
        new(outcome, new ImportSummary(0, 0, 0, 0), [], null);

    public static ImportResult Rejected(string message) =>
        new(ImportOutcome.Rejected, new ImportSummary(0, 0, 0, 0), [], message);

    public static ImportResult Invalid(IEnumerable<ImportIssue> issues) =>
        new(
            ImportOutcome.Invalid,
            new ImportSummary(0, 0, 0, 0),
            [.. issues.Select(issue => new ImportIssueView(issue.Line, issue.Column, issue.Message))],
            null);
}

/// <summary>Template exposto para a interface montar a documentação das colunas.</summary>
public sealed record ImportTemplateView(
    string Kind,
    int Version,
    string Label,
    string FileName,
    IReadOnlyList<ImportColumnView> Columns)
{
    public static ImportTemplateView From(CsvTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new(
            template.Kind.ToString(),
            template.Version,
            template.Label,
            template.FileName,
            [.. template.Columns.Select(column => new ImportColumnView(
                column.Name,
                column.Required,
                column.Description,
                column.Example))]);
    }
}

public sealed record ImportColumnView(string Name, bool Required, string Description, string Example);
