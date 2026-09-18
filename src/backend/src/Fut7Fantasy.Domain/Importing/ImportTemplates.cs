using System.Text;

namespace Fut7Fantasy.Domain.Importing;

/// <summary>O que um arquivo importa.</summary>
public enum ImportKind
{
    Teams = 1,
    Athletes = 2,
    Coaches = 3,
    Matches = 4,
}

/// <param name="Name">Nome exato da coluna no cabeçalho.</param>
/// <param name="Required">Coluna sem a qual o arquivo é recusado.</param>
/// <param name="Description">O que a coluna significa, em linguagem de quem organiza.</param>
/// <param name="Example">Valor de exemplo, usado no arquivo modelo.</param>
public sealed record CsvColumn(string Name, bool Required, string Description, string Example);

/// <summary>
/// Template versionado de importação (03 §13).
///
/// A versão vive no nome do arquivo modelo, não numa linha dentro dele: uma linha extra
/// quebraria a leitura em qualquer planilha. Acrescentar ou renomear coluna gera uma
/// versão nova, e o cabeçalho é o que identifica a versão na prática — um arquivo antigo
/// é recusado pela coluna que falta, com o nome dela na mensagem.
/// </summary>
public sealed class CsvTemplate
{
    /// <summary>Ponto e vírgula porque é o que o Excel em português grava e lê sem ajuste.</summary>
    public const char DefaultDelimiter = ';';

    internal CsvTemplate(
        ImportKind kind,
        int version,
        string label,
        IReadOnlyList<CsvColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> sampleRows)
    {
        Kind = kind;
        Version = version;
        Label = label;
        Columns = columns;
        SampleRows = sampleRows;
    }

    public ImportKind Kind { get; }

    public int Version { get; }

    /// <summary>Nome da coisa importada, para mensagens e para a interface.</summary>
    public string Label { get; }

    public IReadOnlyList<CsvColumn> Columns { get; }

    /// <summary>Linhas de exemplo do arquivo modelo. São fictícias de propósito (01 §11).</summary>
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; }

    public string FileName => $"{Slug}-v{Version}.csv";

    private string Slug => Kind switch
    {
        ImportKind.Teams => "times",
        ImportKind.Athletes => "atletas",
        ImportKind.Coaches => "tecnicos",
        ImportKind.Matches => "partidas",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Tipo de importação desconhecido."),
    };

    /// <summary>Arquivo modelo pronto para abrir na planilha, com as linhas de exemplo.</summary>
    public string Render(char delimiter = DefaultDelimiter)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(delimiter, Columns.Select(column => CsvText.Cell(column.Name, delimiter)));
        builder.Append("\r\n");
        foreach (var row in SampleRows)
        {
            builder.AppendJoin(delimiter, row.Select(cell => CsvText.Cell(cell, delimiter)));
            builder.Append("\r\n");
        }

        return builder.ToString();
    }
}

/// <summary>Catálogo dos templates vigentes.</summary>
public static class ImportTemplates
{
    private static readonly CsvTemplate TeamsV1 = new(
        ImportKind.Teams,
        1,
        "times",
        [
            new("nome", true, "Nome do time, único no campeonato. É por ele que o atleta aponta.", "União da Vila"),
        ],
        [
            ["União da Vila"],
            ["Estrela do Bairro"],
            ["Grêmio da Rua 9"],
        ]);

    private static readonly CsvTemplate AthletesV1 = new(
        ImportKind.Athletes,
        1,
        "atletas",
        [
            new("nome_esportivo", true, "Como o atleta aparece no jogo. Único no campeonato.", "Bia"),
            new("time", true, "Nome exato de um time já cadastrado ou importado antes.", "União da Vila"),
            new("posicao", true, "goleiro, defensor, meio-campista ou atacante.", "meio-campista"),
            new("nivel_preco", false, "destaque, regular ou basico. Vazio vira regular.", "destaque"),
            new(
                "preco_exato",
                false,
                "Preço em créditos, de 1 a 30, com vírgula ou ponto. Vazio usa o nível.",
                "10,5"),
        ],
        [
            ["Bia", "União da Vila", "meio-campista", "destaque", ""],
            ["Nena", "União da Vila", "goleiro", "regular", ""],
            ["Tatá", "Estrela do Bairro", "atacante", "basico", "6,5"],
        ]);

    private static readonly CsvTemplate CoachesV1 = new(
        ImportKind.Coaches,
        1,
        "técnicos",
        [
            new("time", true, "Nome exato do time. Cada time já tem um técnico desde que foi criado.", "União da Vila"),
            new(
                "nome",
                false,
                "Nome da pessoa. Vazio mantém o técnico sem pessoa identificada.",
                "Seu Zé"),
            new("nivel_preco", false, "destaque, regular ou basico. Vazio vira regular.", "regular"),
            new("preco_exato", false, "Preço em créditos, de 1 a 30. Vazio usa o nível.", ""),
        ],
        [
            ["União da Vila", "Seu Zé", "destaque", ""],
            ["Estrela do Bairro", "", "regular", ""],
            ["Grêmio da Rua 9", "Dona Val", "basico", "5"],
        ]);

    /// <summary>
    /// As partidas apontam para rodada, fase e times pelo nome. A rodada que ainda não
    /// existe é criada em rascunho; a fase e os times precisam existir antes.
    /// </summary>
    private static readonly CsvTemplate MatchesV1 = new(
        ImportKind.Matches,
        1,
        "partidas",
        [
            new(
                "rodada",
                true,
                "Nome da rodada. Se ainda não existir, é criada em rascunho no fim da ordem.",
                "Rodada 1"),
            new("fase", true, "Nome exato de uma fase já cadastrada.", "Primeira fase"),
            new("mandante", true, "Nome exato do time mandante, confirmado na fase.", "União da Vila"),
            new("visitante", true, "Nome exato do time visitante, confirmado na fase.", "Estrela do Bairro"),
            new("data", true, "Dia do jogo, como 20/09/2026.", "20/09/2026"),
            new("hora", true, "Hora do início no fuso do campeonato, como 09:30.", "09:30"),
        ],
        [
            ["Rodada 1", "Primeira fase", "União da Vila", "Estrela do Bairro", "20/09/2026", "09:30"],
            ["Rodada 1", "Primeira fase", "Grêmio da Rua 9", "União da Vila", "20/09/2026", "11:00"],
            ["Rodada 2", "Primeira fase", "Estrela do Bairro", "Grêmio da Rua 9", "27/09/2026", "09:30"],
        ]);

    public static IReadOnlyList<CsvTemplate> Current { get; } = [TeamsV1, AthletesV1, CoachesV1, MatchesV1];

    public static CsvTemplate For(ImportKind kind) =>
        Current.SingleOrDefault(template => template.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de importação sem template.");
}
