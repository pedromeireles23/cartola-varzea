using System.Globalization;
using Fut7Fantasy.Domain.Competitions;
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

/// <summary>
/// Partida lida do arquivo. <see cref="KickoffLocal"/> fica no formato `2026-09-20T09:30`
/// e no fuso do campeonato; a conversão para UTC é do servidor, que conhece o fuso.
/// </summary>
public sealed record MatchImportRow(
    int Line,
    string RoundName,
    string StageName,
    string HomeTeamName,
    string AwayTeamName,
    string KickoffLocal);

/// <summary>
/// Uma linha de estatística: um atleta num jogo da rodada. O jogo é apontado por
/// mandante e visitante; <see cref="GoalsConceded"/> nulo significa "calcular", o que só
/// funciona com um goleiro só no time.
/// </summary>
public sealed record MatchStatisticsImportRow(
    int Line,
    string HomeTeamName,
    string AwayTeamName,
    string AthleteName,
    string? TeamName,
    bool DidPlay,
    bool PlayedAsGoalkeeper,
    int? GoalsConceded,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    RedCardReason? RedCard,
    int OwnGoals,
    int PenaltyMisses);

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

    private static readonly Dictionary<string, bool> YesNo = new(StringComparer.Ordinal)
    {
        ["sim"] = true,
        ["s"] = true,
        ["nao"] = false,
        ["n"] = false,
    };

    private static readonly Dictionary<string, RedCardReason> RedCards = new(StringComparer.Ordinal)
    {
        ["direto"] = RedCardReason.Direct,
        ["segundo amarelo"] = RedCardReason.SecondYellow,
        ["segundo_amarelo"] = RedCardReason.SecondYellow,
    };

    /// <summary>Coluna da planilha de cada campo da súmula, para o erro apontar a célula.</summary>
    private static readonly Dictionary<string, string> StatisticsColumns = new(StringComparer.Ordinal)
    {
        [nameof(MatchSheetAppearanceDefinition.DidPlay)] = "jogou",
        [nameof(MatchSheetAppearanceDefinition.PlayedAsGoalkeeper)] = "goleiro",
        [nameof(MatchSheetAppearanceDefinition.GoalsConceded)] = "gols_sofridos",
        [nameof(MatchSheetAppearanceDefinition.Goals)] = "gols",
        [nameof(MatchSheetAppearanceDefinition.Assists)] = "assistencias",
        [nameof(MatchSheetAppearanceDefinition.GoalkeeperSaves)] = "defesas",
        [nameof(MatchSheetAppearanceDefinition.PenaltySaves)] = "penaltis_defendidos",
        [nameof(MatchSheetAppearanceDefinition.YellowCards)] = "amarelos",
        [nameof(MatchSheetAppearanceDefinition.RedCards)] = "vermelho",
        [nameof(MatchSheetAppearanceDefinition.RedCardReason)] = "vermelho",
        [nameof(MatchSheetAppearanceDefinition.OwnGoals)] = "gols_contra",
        [nameof(MatchSheetAppearanceDefinition.PenaltyMisses)] = "penaltis_perdidos",
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

    /// <summary>Coluna do arquivo de estatísticas que corresponde a um campo da súmula.</summary>
    public static string StatisticsColumnFor(string field) =>
        StatisticsColumns.GetValueOrDefault(field, "atleta");

    /// <summary>
    /// Estatísticas de uma rodada. Aqui só se lê o que foi escrito; as regras da súmula
    /// (cartões, goleiro, placar) são do domínio e rodam depois, por partida.
    /// </summary>
    public static ImportParseResult<MatchStatisticsImportRow> MatchStatistics(CsvDocument document)
    {
        var template = ImportTemplates.For(ImportKind.MatchStatistics);
        return Map(template, document, (row, cells, issues) =>
        {
            var home = Required(
                cells, "mandante", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var away = Required(
                cells, "visitante", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var athlete = Required(
                cells,
                "atleta",
                row,
                issues,
                AthleteDefinition.SportingNameMinLength,
                AthleteDefinition.SportingNameMaxLength);
            var team = Optional(
                cells, "time", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var didPlay = Enumerated(cells, "jogou", row, issues, YesNo, required: true);
            var goalkeeper = Enumerated(cells, "goleiro", row, issues, YesNo, required: false) ?? false;
            var conceded = Count(cells, "gols_sofridos", row, issues, emptyIsZero: false);
            var goals = Count(cells, "gols", row, issues);
            var assists = Count(cells, "assistencias", row, issues);
            var saves = Count(cells, "defesas", row, issues);
            var penaltySaves = Count(cells, "penaltis_defendidos", row, issues);
            var yellows = Count(cells, "amarelos", row, issues);
            var red = Enumerated(cells, "vermelho", row, issues, RedCards, required: false);
            var ownGoals = Count(cells, "gols_contra", row, issues);
            var penaltyMisses = Count(cells, "penaltis_perdidos", row, issues);

            return home is null || away is null || athlete is null || didPlay is null
                ? null
                : new MatchStatisticsImportRow(
                    row.Line,
                    home,
                    away,
                    athlete,
                    team,
                    didPlay.Value,
                    goalkeeper,
                    conceded,
                    goals ?? 0,
                    assists ?? 0,
                    saves ?? 0,
                    penaltySaves ?? 0,
                    yellows ?? 0,
                    red,
                    ownGoals ?? 0,
                    penaltyMisses ?? 0);
        }, row => $"{row.AthleteName} em {row.HomeTeamName} x {row.AwayTeamName}", "atleta");
    }

    /// <summary>
    /// A identidade da partida é rodada, mandante e visitante: reenviar o arquivo com outro
    /// horário remarca o jogo em vez de criar um segundo.
    /// </summary>
    public static ImportParseResult<MatchImportRow> Matches(CsvDocument document)
    {
        var template = ImportTemplates.For(ImportKind.Matches);
        return Map(template, document, (row, cells, issues) =>
        {
            var round = Required(
                cells, "rodada", row, issues, RoundDefinition.NameMinLength, RoundDefinition.NameMaxLength);
            var stage = Required(
                cells, "fase", row, issues, StageDefinition.NameMinLength, StageDefinition.NameMaxLength);
            var home = Required(
                cells, "mandante", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var away = Required(
                cells, "visitante", row, issues, RealTeamDefinition.NameMinLength, RealTeamDefinition.NameMaxLength);
            var date = Date(cells, "data", row, issues);
            var time = Time(cells, "hora", row, issues);

            if (home is not null && away is not null && string.Equals(home, away, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(row.Line, "visitante", "Um time não joga contra ele mesmo."));
                return null;
            }

            return round is null || stage is null || home is null || away is null || date is null || time is null
                ? null
                : new MatchImportRow(
                    row.Line,
                    round,
                    stage,
                    home,
                    away,
                    date.Value.ToDateTime(time.Value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        }, row => $"{row.HomeTeamName} x {row.AwayTeamName} na {row.RoundName}", "mandante");
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
        MatchImportRow match => match.Line,
        MatchStatisticsImportRow statistics => statistics.Line,
        _ => 0,
    };

    /// <summary>
    /// Quantidade inteira de 0 a 999. Vazio vale 0, a não ser quando vazio tem outro
    /// sentido, como os gols sofridos que o servidor calcula.
    /// </summary>
    private static int? Count(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues,
        bool emptyIsZero = true)
    {
        var raw = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (raw.Length == 0)
        {
            return emptyIsZero ? 0 : null;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= 999)
        {
            return value;
        }

        issues.Add(new(row.Line, column, $"`{raw}` não vale em `{column}`. Use um número inteiro, como 0 ou 2."));
        return null;
    }

    /// <summary>`20/09/2026` é o que a planilha em português grava; `2026-09-20` também vale.</summary>
    private static DateOnly? Date(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues)
    {
        var raw = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (DateOnly.TryParseExact(
            raw,
            ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return date;
        }

        issues.Add(new(
            row.Line,
            column,
            raw.Length == 0 ? $"Preencha `{column}`." : $"`{raw}` não é uma data. Use algo como 20/09/2026."));
        return null;
    }

    /// <summary>Aceita os segundos que a planilha às vezes acrescenta ao salvar.</summary>
    private static TimeOnly? Time(
        IReadOnlyDictionary<string, string> cells,
        string column,
        CsvRow row,
        List<ImportIssue> issues)
    {
        var raw = cells.GetValueOrDefault(column, string.Empty).Trim();
        if (TimeOnly.TryParseExact(
            raw,
            ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var time))
        {
            return new TimeOnly(time.Hour, time.Minute);
        }

        issues.Add(new(
            row.Line,
            column,
            raw.Length == 0 ? $"Preencha `{column}`." : $"`{raw}` não é uma hora. Use algo como 09:30."));
        return null;
    }

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
