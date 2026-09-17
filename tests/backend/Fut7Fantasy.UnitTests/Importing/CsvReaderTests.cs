using System.Text;
using Fut7Fantasy.Domain.Importing;

namespace Fut7Fantasy.UnitTests.Importing;

public sealed class CsvReaderTests
{
    [Fact]
    public void ReadsHeaderAndRowsKeepingTheLineNumberOfTheFile()
    {
        var document = Read("nome;posicao\r\nBia;meio-campista\r\nNena;goleiro\r\n");

        Assert.Equal(["nome", "posicao"], document.Header);
        Assert.Equal([2, 3], document.Rows.Select(row => row.Line));
        Assert.Equal(["Bia", "meio-campista"], document.Rows[0].Cells);
    }

    [Theory]
    [InlineData("nome;time\r\nBia;União\r\n", ';')]
    [InlineData("nome,time\r\nBia,União\r\n", ',')]
    public void AcceptsCommaAndSemicolonBecauseThatIsWhatSpreadsheetsWrite(string content, char expected)
    {
        Assert.Equal(expected, Read(content).Delimiter);
    }

    [Fact]
    public void QuotedFieldKeepsDelimiterLineBreakAndDoubledQuotes()
    {
        var document = Read("nome;apelido\r\n\"Silva; Jr\";\"O \"\"Rei\"\"\"\r\n");

        Assert.Equal(["Silva; Jr", "O \"Rei\""], document.Rows[0].Cells);
    }

    [Fact]
    public void HeaderIsComparedWithoutAccentCaseOrSpace()
    {
        var document = Read("Nome Esportivo;POSIÇÃO\r\nBia;meia\r\n");

        Assert.Equal(["nome_esportivo", "posicao"], document.Header);
    }

    [Fact]
    public void BlankLinesAreDroppedInsteadOfRejectingTheFile()
    {
        // Planilha exportada termina com linha vazia; recusar por isso seria hostil.
        var document = Read("nome\r\nBia\r\n\r\n;\r\nNena\r\n\r\n");

        Assert.Equal(["Bia", "Nena"], document.Rows.Select(row => row.Cells[0]));
    }

    [Fact]
    public void ByteOrderMarkIsNotPartOfTheFirstColumnName()
    {
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("nome\r\nBia\r\n")).ToArray();

        var result = CsvReader.Read(content);

        Assert.Equal(["nome"], result.Document!.Header);
    }

    [Fact]
    public void FormulaIsPlainTextForTheReader()
    {
        // Ler não interpreta nada; a neutralização acontece ao exportar.
        var document = Read("nome\r\n=1+1\r\n");

        Assert.Equal("=1+1", document.Rows[0].Cells[0]);
    }

    [Fact]
    public void FileLargerThanTheLimitIsRejectedBeforeBeingDecoded()
    {
        var result = CsvReader.Read(new byte[CsvLimits.MaxBytes + 1]);

        Assert.Equal(CsvFailure.TooLarge, result.Failure);
        Assert.Null(result.Document);
    }

    [Fact]
    public void TooManyRowsIsRejected()
    {
        var content = new StringBuilder("nome\r\n");
        for (var row = 0; row <= CsvLimits.MaxRows; row++)
        {
            content.Append("Atleta ").Append(row).Append("\r\n");
        }

        Assert.Equal(CsvFailure.TooManyRows, CsvReader.Read(Bytes(content.ToString())).Failure);
    }

    [Fact]
    public void TooManyColumnsAndOversizedCellPointAtTheLine()
    {
        var wide = string.Join(';', Enumerable.Range(0, CsvLimits.MaxColumns + 1));
        var columns = CsvReader.Read(Bytes($"nome\r\n{wide}\r\n"));
        Assert.Equal(CsvFailure.TooManyColumns, columns.Failure);
        Assert.Equal(2, columns.Line);

        var long_ = new string('a', CsvLimits.MaxCellLength + 1);
        var cell = CsvReader.Read(Bytes($"nome\r\nBia\r\n{long_}\r\n"));
        Assert.Equal(CsvFailure.CellTooLong, cell.Failure);
        Assert.Equal(3, cell.Line);
    }

    [Fact]
    public void NonUtf8FileIsRejectedInsteadOfImportingBrokenAccents()
    {
        // "João" em Latin-1: importar isso calado viraria "Jo?o" no catálogo.
        var latin1 = Encoding.Latin1.GetBytes("nome\r\nJoão\r\n");

        Assert.Equal(CsvFailure.InvalidEncoding, CsvReader.Read(latin1).Failure);
    }

    [Fact]
    public void UnterminatedQuoteIsRejectedWithTheLine()
    {
        var result = CsvReader.Read(Bytes("nome\r\n\"Bia\r\n"));

        Assert.Equal(CsvFailure.UnterminatedQuote, result.Failure);
    }

    [Fact]
    public void EmptyFileIsRejected()
    {
        Assert.Equal(CsvFailure.Empty, CsvReader.Read(Bytes("\r\n\r\n")).Failure);
    }

    [Fact]
    public void FileWithoutTrailingNewLineStillHasItsLastRow()
    {
        var document = Read("nome\r\nBia");

        Assert.Equal(["Bia"], document.Rows.Select(row => row.Cells[0]));
    }

    private static CsvDocument Read(string content)
    {
        var result = CsvReader.Read(Bytes(content));
        Assert.Null(result.Failure);
        return result.Document!;
    }

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);
}
