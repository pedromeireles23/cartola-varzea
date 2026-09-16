namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>Versões de concorrência otimista trocadas com a interface em base64.</summary>
internal static class RowVersions
{
    /// <summary>
    /// Verdadeiro quando a versão enviada é a atual. Uma versão ilegível nunca é a atual:
    /// vale como edição feita sobre dados velhos.
    /// </summary>
    public static bool Matches(string? version, byte[] current, out byte[] expected)
    {
        expected = [];
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var buffer = new byte[version.Length];
        if (!Convert.TryFromBase64String(version, buffer, out var written))
        {
            return false;
        }

        expected = buffer[..written];
        return expected.AsSpan().SequenceEqual(current);
    }
}
