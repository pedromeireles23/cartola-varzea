using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Fut7Fantasy.IntegrationTests;

/// <summary>
/// O código do aplicativo autenticador (RFC 6238), calculado como o celular calcula. O
/// Identity confere pelo relógio do sistema, e não pelo da aplicação, então o código sai da
/// hora real mesmo com o relógio dos testes parado.
/// </summary>
internal static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Código de seis dígitos da chave, agora ou no instante informado.</summary>
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);

#pragma warning disable CA5350 // O RFC 6238 é HMAC-SHA1, como no aplicativo e no Identity.
        var hash = HMACSHA1.HashData(FromBase32(base32Key), counter);
#pragma warning restore CA5350

        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string key)
    {
        var clean = key.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new byte[clean.Length * 5 / 8];
        int buffer = 0, bits = 0, index = 0;

        foreach (var character in clean)
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return output;
    }
}
