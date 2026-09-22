using System.Security.Cryptography;

namespace Fut7Fantasy.Domain.Leagues;

/// <summary>
/// O código que alguém digita para entrar numa liga privada (01 §9).
///
/// Ele é lido em voz alta e digitado no celular, então o alfabeto não tem as letras que
/// se confundem com números — sem I, L, O, U, nem 0 e 1. São 32 símbolos em 10 posições,
/// mais de 10¹⁵ combinações: tentar adivinhar não é um caminho, e o limite de tentativas
/// na rota fecha o que sobra.
///
/// Ao contrário do convite de auxiliar, o código fica legível no banco: o dono da liga
/// precisa reenviá-lo ao grupo semanas depois, e guardar só o hash o obrigaria a trocar
/// o código toda vez que perdesse a mensagem. O risco aceito está no 04 §19 — quem lê o
/// banco entra numa liga privada e vê um ranking, nada além disso —, e o dono pode
/// rotacionar o código quando quiser.
/// </summary>
public static class LeagueInviteCode
{
    /// <summary>Sem I, L, O, U, 0 e 1: o que sobra não se confunde ao ditar.</summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 10;

    public static string Generate() =>
        RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// O que a pessoa digitou, pronto para comparar: sem espaços nem hífens, em
    /// maiúsculas. Devolve nulo quando não pode ser um código, e aí nem vale consultar.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var cleaned = new string([
            .. typed.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant),
        ]);
        return cleaned.Length == Length && cleaned.All(Alphabet.Contains) ? cleaned : null;
    }
}
