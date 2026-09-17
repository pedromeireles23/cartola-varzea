using System.Text;
using Fut7Fantasy.Domain.Importing;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Importing;

public sealed class ImportParserTests
{
    [Fact]
    public void AthletesAcceptTheVocabularyTheOrganizerWrites()
    {
        var parsed = ImportParser.Athletes(Read(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;Meio-Campista;Destaque;
            Nena;União da Vila;GOLEIRO;;
            Tatá;Estrela do Bairro;atacante;basico;6,5
            Duda;Estrela do Bairro;zagueiro;regular;7.25
            """));

        Assert.True(parsed.IsValid);
        Assert.Equal(
            [Position.Midfielder, Position.Goalkeeper, Position.Forward, Position.Defender],
            parsed.Rows.Select(row => row.Position));

        // Nível vazio é regular, o padrão do produto (01 §9).
        Assert.Equal(PriceTier.Regular, parsed.Rows[1].PriceTier);
        Assert.Equal(PriceTier.Star, parsed.Rows[0].PriceTier);
        Assert.Null(parsed.Rows[0].ExactPrice);

        // Vírgula e ponto decimal: os dois são digitados na prática.
        Assert.Equal(6.5m, parsed.Rows[2].ExactPrice);
        Assert.Equal(7.25m, parsed.Rows[3].ExactPrice);
    }

    [Fact]
    public void EveryBadLineIsReportedAtOnceWithItsLineNumber()
    {
        var parsed = ImportParser.Athletes(Read(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;meio-campista;destaque;
            ;União da Vila;goleiro;;
            Nena;;goleiro;;
            Tatá;União da Vila;pivô;;
            Duda;União da Vila;atacante;craque;
            Rê;União da Vila;atacante;;99
            Lu;União da Vila;atacante;;dez
            """));

        Assert.False(parsed.IsValid);
        Assert.Equal([3, 4, 5, 6, 7, 8], parsed.Issues.Select(issue => issue.Line));
        Assert.Equal(
            ["nome_esportivo", "time", "posicao", "nivel_preco", "preco_exato", "preco_exato"],
            parsed.Issues.Select(issue => issue.Column));

        // A linha boa continua sendo lida: o relatório mostra o arquivo inteiro.
        Assert.Equal(["Bia"], parsed.Rows.Select(row => row.SportingName));
        Assert.Contains("goleiro", Message(parsed, 5), StringComparison.Ordinal);
        Assert.Contains("destaque", Message(parsed, 6), StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedKeyInTheSameFileIsAnErrorAndNeitherRowIsKept()
    {
        var parsed = ImportParser.Teams(Read(
            """
            nome
            União da Vila
            Estrela do Bairro
            união da vila
            """));

        Assert.False(parsed.IsValid);
        Assert.Equal([4], parsed.Issues.Select(issue => issue.Line));
        Assert.Equal(["Estrela do Bairro"], parsed.Rows.Select(row => row.Name));
    }

    [Fact]
    public void MissingRequiredColumnStopsTheFileBeforeTheRows()
    {
        var parsed = ImportParser.Athletes(Read(
            """
            nome_esportivo;posicao
            Bia;meio-campista
            """));

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Rows);
        Assert.Equal(ImportParser.HeaderLine, parsed.Issues[0].Line);
        Assert.Contains("time", parsed.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownColumnSuggestsDownloadingTheCurrentTemplate()
    {
        var parsed = ImportParser.Teams(Read(
            """
            nome;cidade
            União da Vila;São Paulo
            """));

        Assert.False(parsed.IsValid);
        Assert.Contains("modelo atual", parsed.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowWithFewerCellsThanTheHeaderIsReportedNotGuessed()
    {
        var parsed = ImportParser.Athletes(Read(
            """
            nome_esportivo;time;posicao;nivel_preco;preco_exato
            Bia;União da Vila;meio-campista
            """));

        Assert.False(parsed.IsValid);
        Assert.Contains("3 colunas", parsed.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoachNameIsOptionalAndEmptyKeepsTheFallback()
    {
        var parsed = ImportParser.Coaches(Read(
            """
            time;nome;nivel_preco;preco_exato
            União da Vila;Seu Zé;destaque;
            Estrela do Bairro;;;
            """));

        Assert.True(parsed.IsValid);
        Assert.Equal("Seu Zé", parsed.Rows[0].DisplayName);
        Assert.Null(parsed.Rows[1].DisplayName);
        Assert.Equal(PriceTier.Regular, parsed.Rows[1].PriceTier);
    }

    [Fact]
    public void OptionalColumnsMayBeLeftOutOfTheFileEntirely()
    {
        var parsed = ImportParser.Athletes(Read(
            """
            nome_esportivo;time;posicao
            Bia;União da Vila;meio-campista
            """));

        Assert.True(parsed.IsValid);
        Assert.Equal(PriceTier.Regular, parsed.Rows[0].PriceTier);
        Assert.Null(parsed.Rows[0].ExactPrice);
    }

    [Fact]
    public void FormulaInACellIsJustAName()
    {
        var parsed = ImportParser.Teams(Read(
            """
            nome
            =1+1
            """));

        Assert.True(parsed.IsValid);
        Assert.Equal("=1+1", parsed.Rows[0].Name);
    }

    private static string Message(ImportParseResult<AthleteImportRow> parsed, int line) =>
        parsed.Issues.Single(issue => issue.Line == line).Message;

    private static CsvDocument Read(string content)
    {
        var result = CsvReader.Read(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\r\n")));
        Assert.Null(result.Failure);
        return result.Document!;
    }
}
