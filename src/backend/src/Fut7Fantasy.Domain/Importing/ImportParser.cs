using System.Globalization;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Importing;

/// <summary>
/// Um problema numa linha. <see cref="Line"/> é o número que a planilha mostra, para que
/// a pessoa vá direto na linha errada em vez de procurar.
/// </summary>
public sealed record ImportIssue(int Line, string Column, string Message);

public sealed record TeamImportRow(int Line, string Name);

public sealed record AthleteImportRow(
    int Line,
    string SportingName,
    string TeamName,
    Position Position,
    PriceTier PriceTier,
    decimal? ExactPrice);

public sealed record CoachImportRow(
    int Line,
    string TeamName,
    string? DisplayName,
    PriceTier PriceTier,
    decimal? ExactPrice);

/// <summary>Resultado da leitura: as linhas boas e todos os problemas encontrados.</summary>
public sealed record ImportParseResult<TRow>(IReadOnlyList<TRow> Rows, IReadOnlyList<ImportIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Traduz o arquivo lido para linhas do domínio.
///
/// Nunca para no primeiro erro: quem preencheu a planilha precisa ver tudo o que está
/// errado de uma vez, senão corrige uma linha, reenvia e descobre a próxima.
/// </summary>
public static class ImportParser
{
    /// <summary>Linha do cabeçalho, para erros que não pertencem a nenhuma linha de dados.</summary>
    public const int HeaderLine = 1;

    private static readonly Dictionary<string, Position> Positions = new(StringComparer.Ordinal)
    {
        ["goleiro"] = Position.Goalkeeper,
        ["defensor"] = Position.Defender,
        ["zagueiro"] = Position.Defender,
        ["meio-campista"] = Position.Midfielder,
        ["meio campista"] = Position.Midfielder,
        ["meia"] = Position.Midfielder,
        ["atacante"] = Position.Forward,
    };

    private static readonly Dictionary<string, PriceTier> Tiers = new(StringComparer.Ordinal)
    {
        ["destaque"] = PriceTier.Star,
        ["regular"] = PriceTier.Regular,
        ["basico"] = PriceTier.Basic,
    };

    /// <summary>
    /// Confere o cabeçalho contra o template. Coluna obrigatória ausente recusa o arquivo;
    /// coluna desconhecida também, porque costuma ser template errado ou versão antiga.
    /// </summary>
    public static IReadOnlyList<ImportIssue> ValidateHeader(CsvTemplate template, CsvDocument document)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(document);

        List<ImportIssue> issues = [];
        var known = template.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var column in template.Columns.Where(
            column => column.Required && !document.Header.Contains(column.Name, StringComparer.Ordinal)))
        {
            issues.Add(new(HeaderLine, column.Name, $"Falta a coluna obrigatória `{column.Name}`."));
        }

        foreach (var header in document.Header.Where(header => !known.Contains(header)))
        {
            issues.Add(new(
                HeaderLine,
                header,
                $"A coluna `{header}` não pertence a este modelo. Baixe o modelo atual e refaça o arquivo."));
        }

        var duplicated = document.Header
            .GroupBy(header => header, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (var group in duplicated)
        {
            issues.Add(new(HeaderLine, group.Key, $"A coluna `{group.Key}` aparece mais de uma vez."));
        }

        return issues;
    }

    public static ImportParseResult<TeamImportRow> Teams(CsvDocument document)
    {
        var template = ImportTemplates.For(ImportKind.Teams);
        return Map(template, document, (row, cells, issues) =>
        {
            var name = Required(
                cells,
                "nome",
                row,
                issues,
                RealTeamDefinition.NameMinLength,
                RealTeamDefinition.NameMaxLength);
            return name is null ? null : new TeamImportRow(row.Line, name);
        }, row => row.Name, "nome");
    }

    public static ImportParseResult<AthleteImportRow> Athletes(CsvDocument document)
    {
        var template = ImportTemplates.For(ImportKind.Athletes);
        return Map(template, document, (row, cells, issues) =>
        {
            var name = Required(
                cells,
                "nome_esportivo",
                row,
                issues,
                AthleteDefinition.SportingNameMinLength,
                AthleteDefinition.SportingNameMaxLength);
            var team = Required(
                cells, "time", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var position = Enumerated(cells, "posicao", row, issues, Positions, required: true);
            var tier = Enumerated(cells, "nivel_preco", row, issues, Tiers, required: false) ?? PriceTier.Regular;
            var price = Price(cells, "preco_exato", row, issues);

            return name is null || team is null || position is null
                ? null
                : new AthleteImportRow(row.Line, name, team, position.Value, tier, price);
        }, row => row.SportingName, "nome_esportivo");
    }

    public static ImportParseResult<CoachImportRow> Coaches(CsvDocument document)
    {
        var template = ImportTemplates.For(ImportKind.Coaches);
        return Map(template, document, (row, cells, issues) =>
        {
            var team = Required(
                cells, "time", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var name = Optional(
                cells,
                "nome",
                row,
                issues,
                CoachDefinition.DisplayNameMinLength,
                CoachDefinition.DisplayNameMaxLength);
            var tier = Enumerated(cells, "nivel_preco", row, issues, Tiers, required: false) ?? PriceTier.Regular;
            var price = Price(cells, "preco_exato", row, issues);

            return team is null ? null : new CoachImportRow(row.Line, team, name, tier, price);
        }, row => row.TeamName, "time");
    }

    /// <summary>
    /// Percorre as linhas aplicando <paramref name="build"/> e acusa repetição da chave
    /// dentro do próprio arquivo, que é o erro mais comum de planilha copiada.
    /// </summary>
    private static ImportParseResult<TRow> Map<TRow>(
        CsvTemplate template,
        CsvDocument document,
        Func<CsvRow, IReadOnlyDictionary<string, string>, List<ImportIssue>, TRow?> build,
        Func<TRow, string> key,
        string keyColumn)
        where TRow : class
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = ValidateHeader(template, document).ToList();
        if (issues.Count > 0)
        {
            return new ImportParseResult<TRow>([], issues);
        }

        List<TRow> rows = [];
        foreach (var row in document.Rows)
        {
            if (row.Cells.Count != document.Header.Count)
            {
                issues.Add(new(
                    row.Line,
                    keyColumn,
                    $"A linha tem {row.Cells.Count} "
                    + $"{(row.Cells.Count == 1 ? "coluna" : "colunas")} e o modelo tem {document.Header.Count}."));
                continue;
            }

            var cells = document.Header
                .Select((header, index) => (header, value: row.Cells[index]))
                .ToDictionary(item => item.header, item => item.value, StringComparer.Ordinal);

            // Uma linha com qualquer problema fica de fora, mesmo que o problema esteja
            // numa coluna opcional: importar com o padrão esconderia o erro de digitação.
            var before = issues.Count;
            if (build(row, cells, issues) is { } mapped && issues.Count == before)
            {
                rows.Add(mapped);
            }
        }

        var duplicated = rows
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicated)
        {
            foreach (var line in group.Skip(1).Select(item => LineOf(item)))
            {
                issues.Add(new(line, keyColumn, $"`{group.Key}` aparece mais de uma vez no arquivo."));
            }
        }

        var repeated = duplicated.Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ImportParseResult<TRow>(
            repeated.Count == 0 ? rows : [.. rows.Where(row => !repeated.Contains(key(row)))],
            [.. issues.OrderBy(issue => issue.Line)]);
    }

    private static int LineOf<TRow>(TRow row) => row switch
    {
        TeamImportRow team => team.Line,
        AthleteImportRow athlete => athlete.Line,
        CoachImportRow coach => coach.Line,
        _ => 0,
    };

    private static string? Required(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues,
        int minLength,
        int maxLength)
    {
        var value = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (value.Length == 0)
        {
            issues.Add(new(row.Line, column, $"Preencha `{column}`."));
            return null;
        }

        if (value.Length < minLength || value.Length > maxLength)
        {
            issues.Add(new(row.Line, column, $"Use de {minLength} a {maxLength} caracteres em `{column}`."));
            return null;
        }

        return value;
    }

    private static string? Optional(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues,
        int minLength,
        int maxLength)
    {
        var value = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length < minLength || value.Length > maxLength)
        {
            issues.Add(new(row.Line, column, $"Use de {minLength} a {maxLength} caracteres em `{column}`."));
            return null;
        }

        return value;
    }

    private static TValue? Enumerated<TValue>(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues,
        Dictionary<string, TValue> vocabulary,
        bool required)
        where TValue : struct
    {
        var raw = cells.GetValueOrDefault(column, string.Empty);
        var value = CsvText.Normalize(raw);
        if (value.Length == 0)
        {
            if (required)
            {
                issues.Add(new(row.Line, column, $"Preencha `{column}` com {Accepted(vocabulary)}."));
            }

            return null;
        }

        if (vocabulary.TryGetValue(value, out var mapped))
        {
            return mapped;
        }

        issues.Add(new(row.Line, column, $"`{raw.Trim()}` não vale em `{column}`. Use {Accepted(vocabulary)}."));
        return null;
    }

    private static decimal? Price(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues)
    {
        var raw = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        // A planilha em português grava vírgula; digitar ponto também é comum.
        var normalized = raw.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            issues.Add(new(row.Line, column, $"`{raw}` não é um preço. Use algo como 10,5."));
            return null;
        }

        if (!SportsAssetPricing.IsValidExactPrice(price))
        {
            issues.Add(new(
                row.Line,
                column,
                $"O preço vai de {SportsAssetPricing.MinimumPrice:0} a {SportsAssetPricing.MaximumPrice:0} "
                + "créditos, com até duas casas."));
            return null;
        }

        return price;
    }

    /// <summary>Lista os valores aceitos como a pessoa os escreveria: `a`, `b` ou `c`.</summary>
    private static string Accepted<TValue>(Dictionary<string, TValue> vocabulary)
    {
        // Sinônimos existem para aceitar o que a pessoa escreve, mas sugerir todos
        // confundiria: a mensagem oferece um termo por valor.
        var preferred = vocabulary
            .GroupBy(item => item.Value)
            .Select(group => $"`{group.First().Key}`")
            .ToList();
        return preferred.Count == 1
            ? preferred[0]
            : $"{string.Join(", ", preferred[..^1])} ou {preferred[^1]}";
    }
}
