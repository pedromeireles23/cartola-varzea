using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

/// <summary>Endereço público do campeonato (`/c/:campeonato`).</summary>
public sealed class CompetitionSlugTests
{
    [Theory]
    [InlineData("Copa da Várzea", "2026", "copa-da-varzea-2026")]
    [InlineData("  Copa   da   Várzea  ", " 2026 ", "copa-da-varzea-2026")]
    [InlineData("Taça São João!", "2026/27", "taca-sao-joao-2026-27")]
    [InlineData("Liga Ação", "2026", "liga-acao-2026")]
    public void BaseTurnsNameAndSeasonIntoAnAddress(string name, string season, string expected)
    {
        Assert.Equal(expected, CompetitionSlug.Base(name, season));
    }

    [Theory]
    [InlineData("Copa 2026", "2026", "copa-2026")]
    [InlineData("2026 Copa", "2026", "2026-copa")]
    [InlineData("Copa 2026 de Verão", "2026", "copa-2026-de-verao")]
    public void SeasonIsNotRepeatedWhenTheNameAlreadyHasIt(string name, string season, string expected)
    {
        Assert.Equal(expected, CompetitionSlug.Base(name, season));
    }

    [Fact]
    public void SeasonStillEntersWhenItIsOnlyPartOfAWord()
    {
        // "2026" dentro de "2026a" não é a temporada; repetir seria pior que duplicar.
        Assert.Equal("copa-2026a-2026", CompetitionSlug.Base("Copa 2026a", "2026"));
    }

    [Theory]
    [InlineData("!!!", "", "campeonato")]
    [InlineData("漢字", "", "campeonato")]
    [InlineData("A", "", "campeonato-a")]
    public void NameWithoutUsableCharactersFallsBack(string name, string season, string expected)
    {
        Assert.Equal(expected, CompetitionSlug.Base(name, season));
    }

    [Fact]
    public void LongNameIsTruncatedWithoutLeavingATrailingHyphen()
    {
        var slug = CompetitionSlug.Base(new string('a', 60) + " " + new string('b', 40), "2026");

        Assert.Equal(CompetitionSlug.MaxLength, slug.Length);
        Assert.DoesNotContain("--", slug, StringComparison.Ordinal);
        Assert.False(slug.EndsWith('-'));
        Assert.True(CompetitionSlug.IsValid(slug));
    }

    [Fact]
    public void VariantsDisambiguateWithoutPassingTheLimit()
    {
        Assert.Equal("copa-2026", CompetitionSlug.Variant("copa-2026", 1));
        Assert.Equal("copa-2026-2", CompetitionSlug.Variant("copa-2026", 2));
        Assert.Equal("copa-2026-10", CompetitionSlug.Variant("copa-2026", 10));

        var longest = new string('a', CompetitionSlug.MaxLength);
        var variant = CompetitionSlug.Variant(longest, 42);
        Assert.Equal(CompetitionSlug.MaxLength, variant.Length);
        Assert.EndsWith("-42", variant, StringComparison.Ordinal);
        Assert.True(CompetitionSlug.IsValid(variant));
    }

    [Theory]
    [InlineData("copa-da-varzea-2026", true)]
    [InlineData("copa", true)]
    [InlineData("co", false)]
    [InlineData("Copa-2026", false)]
    [InlineData("copa--2026", false)]
    [InlineData("copa-2026-", false)]
    [InlineData("copa da varzea", false)]
    [InlineData("copa/2026", false)]
    [InlineData("copa-várzea", false)]
    [InlineData(null, false)]
    public void ValidAddressesAreLowercaseWordsSeparatedByHyphen(string? slug, bool valid)
    {
        Assert.Equal(valid, CompetitionSlug.IsValid(slug));
    }

    [Fact]
    public void GeneratedAddressesAreAlwaysValid()
    {
        string[] nomes =
        [
            "Copa da Várzea", "Taça!!!", "Ação", "A", "漢字", "Liga  dos  Amigos", new('x', 200),
        ];

        Assert.All(nomes, nome => Assert.True(CompetitionSlug.IsValid(CompetitionSlug.Base(nome, "2026"))));
    }
}
