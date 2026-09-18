using Fut7Fantasy.Domain.Importing;

namespace Fut7Fantasy.UnitTests.Importing;

public sealed class ImportTemplateTests
{
    [Theory]
    [InlineData(ImportKind.Teams, "times-v1.csv")]
    [InlineData(ImportKind.Athletes, "atletas-v1.csv")]
    [InlineData(ImportKind.Coaches, "tecnicos-v1.csv")]
    [InlineData(ImportKind.Matches, "partidas-v1.csv")]
    public void TemplateCarriesItsVersionInTheFileName(ImportKind kind, string fileName)
    {
        Assert.Equal(fileName, ImportTemplates.For(kind).FileName);
    }

    [Fact]
    public void EveryTemplateIsReadBackByTheParserItBelongsTo()
    {
        // O modelo que a pessoa baixa precisa importar sem edição nenhuma.
        var teams = ImportParser.Teams(Document(ImportKind.Teams));
        var athletes = ImportParser.Athletes(Document(ImportKind.Athletes));
        var coaches = ImportParser.Coaches(Document(ImportKind.Coaches));
        var matches = ImportParser.Matches(Document(ImportKind.Matches));

        Assert.Empty(teams.Issues);
        Assert.Empty(athletes.Issues);
        Assert.Empty(coaches.Issues);
        Assert.Empty(matches.Issues);
        Assert.Equal(ImportTemplates.For(ImportKind.Teams).SampleRows.Count, teams.Rows.Count);
        Assert.Equal(ImportTemplates.For(ImportKind.Athletes).SampleRows.Count, athletes.Rows.Count);
        Assert.Equal(ImportTemplates.For(ImportKind.Coaches).SampleRows.Count, coaches.Rows.Count);
        Assert.Equal(ImportTemplates.For(ImportKind.Matches).SampleRows.Count, matches.Rows.Count);
    }

    [Fact]
    public void SampleRowsPointAtTeamsThatTheTeamTemplateCreates()
    {
        // Baixar os três modelos e importar na ordem precisa funcionar sem editar nada.
        var teams = ImportParser.Teams(Document(ImportKind.Teams)).Rows
            .Select(row => row.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(
            ImportParser.Athletes(Document(ImportKind.Athletes)).Rows,
            row => Assert.Contains(row.TeamName, teams));
        Assert.All(
            ImportParser.Coaches(Document(ImportKind.Coaches)).Rows,
            row => Assert.Contains(row.TeamName, teams));
        Assert.All(
            ImportParser.Matches(Document(ImportKind.Matches)).Rows,
            row =>
            {
                Assert.Contains(row.HomeTeamName, teams);
                Assert.Contains(row.AwayTeamName, teams);
            });
    }

    [Fact]
    public void SampleRowsFillEveryColumnOfTheTemplate()
    {
        Assert.All(ImportTemplates.Current, template =>
            Assert.All(template.SampleRows, row => Assert.Equal(template.Columns.Count, row.Count)));
    }

    [Fact]
    public void ExportedCellStartingWithFormulaCharacterIsNeutralized()
    {
        // 04 §9: a planilha não pode executar nada ao abrir um arquivo que nós geramos.
        Assert.Equal("'=1+1", CsvText.Cell("=1+1", ';'));
        Assert.Equal("'+55", CsvText.Cell("+55", ';'));
        Assert.Equal("'-1", CsvText.Cell("-1", ';'));
        Assert.Equal("'@aqui", CsvText.Cell("@aqui", ';'));
        Assert.Equal("União", CsvText.Cell("União", ';'));
    }

    [Fact]
    public void ExportedCellQuotesDelimiterQuoteAndLineBreak()
    {
        Assert.Equal("\"Silva; Jr\"", CsvText.Cell("Silva; Jr", ';'));
        Assert.Equal("\"O \"\"Rei\"\"\"", CsvText.Cell("O \"Rei\"", ';'));
        Assert.Equal("\"duas\nlinhas\"", CsvText.Cell("duas\nlinhas", ';'));
        Assert.Equal("Silva, Jr", CsvText.Cell("Silva, Jr", ';'));
    }

    private static CsvDocument Document(ImportKind kind)
    {
        var result = CsvReader.Read(
            System.Text.Encoding.UTF8.GetBytes(ImportTemplates.For(kind).Render()));
        Assert.Null(result.Failure);
        return result.Document!;
    }
}
