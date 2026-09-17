using System.Text;

namespace Fut7Fantasy.Domain.Importing;

/// <summary>Uma linha de dados com o número da linha no arquivo, como o editor mostra.</summary>
public sealed record CsvRow(int Line, IReadOnlyList<string> Cells);

/// <summary>Arquivo lido: cabeçalho normalizado, linhas e o separador encontrado.</summary>
public sealed record CsvDocument(
    IReadOnlyList<string> Header,
    IReadOnlyList<CsvRow> Rows,
    char Delimiter);

/// <summary>Motivo pelo qual um arquivo inteiro foi recusado, antes de olhar conteúdo.</summary>
public enum CsvFailure
{
    Empty,
    TooLarge,
    TooManyRows,
    TooManyColumns,
    CellTooLong,
    InvalidEncoding,
    UnterminatedQuote,
}

public sealed record CsvReadResult(CsvDocument? Document, CsvFailure? Failure, int Line)
{
    public static CsvReadResult Ok(CsvDocument document) => new(document, null, 0);

    public static CsvReadResult Rejected(CsvFailure failure, int line = 0) => new(null, failure, line);
}

/// <summary>
/// Leitor de CSV próprio, deliberadamente estrito e sem dependência externa (03 §13).
///
/// Ele só reconhece texto: aspas, separador e quebra de linha. Fórmula, macro e
/// referência externa não existem para este leitor — o que vier depois de `=` é uma
/// string como qualquer outra, e a neutralização acontece na exportação
/// (<see cref="CsvText.Cell"/>).
///
/// Aceita vírgula ou ponto e vírgula porque o Excel em português salva com ponto e
/// vírgula, e exigir o contrário só produziria arquivo recusado sem motivo aparente.
/// </summary>
public static class CsvReader
{
    /// <summary>Ponto e vírgula primeiro: no empate, vence o separador dos modelos.</summary>
    private static readonly char[] Delimiters = [';', ','];

    public static CsvReadResult Read(ReadOnlySpan<byte> content)
    {
        if (content.Length > CsvLimits.MaxBytes)
        {
            return CsvReadResult.Rejected(CsvFailure.TooLarge);
        }

        if (!TryDecode(content, out var text))
        {
            return CsvReadResult.Rejected(CsvFailure.InvalidEncoding);
        }

        return Parse(text);
    }

    /// <summary>
    /// Decodifica como UTF-8 estrito, com ou sem BOM. Um arquivo salvo em ANSI falha
    /// aqui, e isso é melhor do que importar "João" como "JoÃ£o" sem ninguém perceber.
    /// </summary>
    private static bool TryDecode(ReadOnlySpan<byte> content, out string text)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        var body = content.StartsWith(bom) ? content[bom.Length..] : content;

        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static CsvReadResult Parse(string text)
    {
        var delimiter = DetectDelimiter(text);
        List<CsvRow> lines = [];
        var cells = new List<string>();
        var cell = new StringBuilder();
        var line = 1;
        var quoted = false;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    cell.Append(character);
                    index++;
                    continue;
                }

                // Aspas dobradas dentro do campo são uma aspa literal.
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index += 2;
                    continue;
                }

                quoted = false;
                index++;
                continue;
            }

            if (character == '"' && cell.Length == 0)
            {
                quoted = true;
                index++;
                continue;
            }

            if (character == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                index++;
                continue;
            }

            if (character is '\r' or '\n')
            {
                index += character == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                cells.Add(cell.ToString());
                cell.Clear();
                if (Close(lines, cells, line) is { } failure)
                {
                    return failure;
                }

                cells = [];
                line++;
                continue;
            }

            cell.Append(character);
            index++;
        }

        if (quoted)
        {
            return CsvReadResult.Rejected(CsvFailure.UnterminatedQuote, line);
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            if (Close(lines, cells, line) is { } failure)
            {
                return failure;
            }
        }

        if (lines.Count == 0)
        {
            return CsvReadResult.Rejected(CsvFailure.Empty);
        }

        var header = lines[0].Cells.Select(Normalize).ToArray();
        var rows = lines.Skip(1).ToArray();
        return rows.Length > CsvLimits.MaxRows
            ? CsvReadResult.Rejected(CsvFailure.TooManyRows)
            : CsvReadResult.Ok(new CsvDocument(header, rows, delimiter));
    }

    /// <summary>
    /// Fecha a linha corrente. Linha totalmente vazia é descartada: planilha exportada
    /// costuma terminar com uma, e recusar o arquivo por isso seria hostil.
    /// </summary>
    private static CsvReadResult? Close(List<CsvRow> lines, List<string> cells, int line)
    {
        if (cells.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        if (cells.Count > CsvLimits.MaxColumns)
        {
            return CsvReadResult.Rejected(CsvFailure.TooManyColumns, line);
        }

        if (cells.Any(item => item.Length > CsvLimits.MaxCellLength))
        {
            return CsvReadResult.Rejected(CsvFailure.CellTooLong, line);
        }

        lines.Add(new CsvRow(line, [.. cells]));
        return null;
    }

    /// <summary>
    /// O separador é o que aparecer mais vezes na primeira linha. Um nome com vírgula
    /// dentro de aspas não confunde: o cabeçalho do template não tem aspas.
    ///
    /// Num arquivo de uma coluna só não há evidência nenhuma, e aí vale o ponto e vírgula
    /// dos modelos — assim `União, Vila` continua sendo um nome, e não duas colunas.
    /// </summary>
    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var firstLine = end < 0 ? text : text[..end];
        return Delimiters.MaxBy(delimiter => firstLine.Count(character => character == delimiter));
    }

    /// <summary>Cabeçalho comparado sem espaço nas pontas, sem acento e em minúsculas.</summary>
    public static string Normalize(string value) =>
        CsvText.Normalize(value).Replace(' ', '_');
}
