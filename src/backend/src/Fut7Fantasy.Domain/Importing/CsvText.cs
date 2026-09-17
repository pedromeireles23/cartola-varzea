using System.Globalization;
using System.Text;

namespace Fut7Fantasy.Domain.Importing;

/// <summary>Comparação e escrita de texto do CSV, do lado de quem preenche a planilha.</summary>
public static class CsvText
{
    /// <summary>
    /// Caracteres que fazem uma planilha tratar a célula como fórmula ao abrir o arquivo
    /// (04 §9). Não importa o que venha depois: o problema é começar por eles.
    /// </summary>
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Minúsculas, sem acento e sem espaço nas pontas. Serve para comparar o que a pessoa
    /// escreveu com o que o template espera: `Meio-Campista`, `meio-campista` e
    /// `MEIO-CAMPISTA` são a mesma coisa para quem preenche a planilha.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Prepara a célula para exportação: neutraliza fórmula com apóstrofo e aplica as
    /// aspas do CSV quando o conteúdo contém separador, aspas ou quebra de linha.
    /// </summary>
    public static string Cell(string? value, char delimiter)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && FormulaStarters.Contains(text[0]))
        {
            text = $"'{text}";
        }

        var needsQuotes = text.Contains(delimiter)
            || text.Contains('"')
            || text.Contains('\n')
            || text.Contains('\r');
        return needsQuotes ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }
}
