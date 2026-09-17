using System.Globalization;
using System.Text;

namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Identificador do campeonato na URL pública (`/c/:campeonato`).
///
/// É gerado na primeira publicação e não muda depois: renomear o campeonato não pode
/// quebrar o link que já foi compartilhado no grupo do campeonato. Por isso o slug é
/// dado uma vez e nunca derivado do nome atual na hora de responder.
/// </summary>
public static class CompetitionSlug
{
    public const int MaxLength = 80;

    /// <summary>Menor slug aceitável; um nome só de símbolos cai no fallback.</summary>
    public const int MinLength = 3;

    /// <summary>Usado quando o nome e a temporada não sobra letra nem número nenhum.</summary>
    public const string Fallback = "campeonato";

    /// <summary>
    /// Base do slug a partir do nome e da temporada. A temporada só entra quando ainda
    /// não aparece no nome, para não gerar `copa-da-varzea-2026-2026`.
    /// </summary>
    public static string Base(string? name, string? season)
    {
        var slug = Normalize(name);
        var seasonSlug = Normalize(season);
        if (seasonSlug.Length > 0 && !ContainsSegment(slug, seasonSlug))
        {
            slug = slug.Length > 0 ? $"{slug}-{seasonSlug}" : seasonSlug;
        }

        if (slug.Length < MinLength)
        {
            slug = slug.Length > 0 ? $"{Fallback}-{slug}" : Fallback;
        }

        return Truncate(slug, MaxLength);
    }

    /// <summary>
    /// Variante <paramref name="attempt"/> da base, para quando o slug já existe. A
    /// primeira tentativa é a própria base; as seguintes recebem `-2`, `-3` e assim por
    /// diante, encurtando a base o suficiente para o sufixo caber.
    /// </summary>
    public static string Variant(string baseSlug, int attempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSlug);
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "A tentativa começa em 1.");
        }

        if (attempt == 1)
        {
            return baseSlug;
        }

        var suffix = $"-{attempt}";
        return Truncate(baseSlug, MaxLength - suffix.Length) + suffix;
    }

    /// <summary>Verdadeiro para o formato que a URL pública aceita.</summary>
    public static bool IsValid(string? slug) =>
        slug is not null
        && slug.Length is >= MinLength and <= MaxLength
        && slug == Normalize(slug);

    /// <summary>
    /// Minúsculas sem acento, com hífen entre as palavras. Acentos são removidos por
    /// decomposição Unicode, e não por tabela, para que "Ação" e "Açao" cheguem ao mesmo
    /// slug em qualquer sistema.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                // Letras fora do ASCII (ж, 漢) somem junto da pontuação: o slug é uma URL.
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    private static bool ContainsSegment(string slug, string segment) =>
        slug == segment
        || slug.StartsWith($"{segment}-", StringComparison.Ordinal)
        || slug.EndsWith($"-{segment}", StringComparison.Ordinal)
        || slug.Contains($"-{segment}-", StringComparison.Ordinal);

    private static string Truncate(string slug, int maxLength) =>
        slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
}
