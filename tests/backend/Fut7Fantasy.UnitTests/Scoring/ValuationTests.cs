using System.Globalization;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Scoring;

/// <summary>
/// Testes de ouro da valorização relativa à posição (01 §9). A tabela de exemplos
/// aprovada e os limites exatos das faixas estão aqui como foram escritos.
/// </summary>
public sealed class ValuationTests
{
    private static readonly ScoringRuleSet Fut7 = ScoringRuleSets.CurrentFor(Modality.Fut7);

    [Theory]
    [InlineData("-1.89", "-0.5")]
    [InlineData("6.11", "1.5")]
    [InlineData("11.11", "2")]
    [InlineData("-3.39", "-1")]
    [InlineData("1.61", "0.5")]
    [InlineData("6.61", "1.5")]
    [InlineData("-5.52", "-1")]
    [InlineData("1.48", "0.5")]
    [InlineData("0.35", "0")]
    public void ApprovedExamplesMoveThePriceAsWritten(string difference, string variation) =>
        Assert.Equal(D(variation), Fut7.Valuation.VariationFor(D(difference)));

    [Theory]
    [InlineData("-6.01", "-2")]
    [InlineData("-6", "-2")]
    [InlineData("-5.99", "-1")]
    [InlineData("-3", "-1")]
    [InlineData("-2.99", "-0.5")]
    [InlineData("-1", "-0.5")]
    [InlineData("-0.99", "0")]
    [InlineData("0", "0")]
    [InlineData("0.99", "0")]
    [InlineData("1", "0.5")]
    [InlineData("2.99", "0.5")]
    [InlineData("3", "1")]
    [InlineData("5.99", "1")]
    [InlineData("6", "1.5")]
    [InlineData("8.99", "1.5")]
    [InlineData("9", "2")]
    [InlineData("40", "2")]
    public void BandsAreHalfOpenWithoutGaps(string difference, string variation) =>
        Assert.Equal(D(variation), Fut7.Valuation.VariationFor(D(difference)));

    [Theory]
    [InlineData(Modality.Fut7)]
    [InlineData(Modality.Futsal)]
    [InlineData(Modality.Field)]
    public void BandsFloorAndCeilingAreTheSameInEveryModality(Modality modality)
    {
        var bands = ScoringRuleSets.CurrentFor(modality).Valuation;

        Assert.Equal(1m, bands.Floor);
        Assert.Equal(30m, bands.Ceiling);
        Assert.Equal(-2m, bands.VariationFor(-6m));
        Assert.Equal(2m, bands.VariationFor(9m));
    }

    [Fact]
    public void AverageOfThePositionUsesOnlyWhoPlayedAndIsRounded()
    {
        // Defensores com 0, 0 e 1 ponto: média 0,333… vira 0,33. O que não jogou não
        // puxa a média para baixo nem muda de preço.
        ValuationInput[] defenders =
        [
            Defender(0m), Defender(0m), Defender(1m), Defender(0m, played: false),
        ];

        var averages = Valuation.Averages(defenders);

        Assert.Equal(0.33m, averages[ValuationGroup.Of(AssetKind.Athlete, Position.Defender)]);
        Assert.Single(averages);
    }

    [Fact]
    public void EachAssetIsComparedWithItsOwnPositionAndCoachesAmongThemselves()
    {
        ValuationInput[] assets =
        [
            Defender(13m, price: 7m),
            Defender(0m, price: 7m),
            Defender(-2m, price: 7m),
            new(AssetKind.Athlete, Guid.NewGuid(), Position.Forward, true, 0m, 9m),
            new(AssetKind.Coach, Guid.NewGuid(), null, true, 3m, 8m),
            new(AssetKind.Coach, Guid.NewGuid(), null, true, 2.3m, 8m),
            Defender(20m, played: false, price: 7m),
        ];

        var changes = Valuation.Apply(Fut7, assets);

        // Defensores: média (13 + 0 - 2) ÷ 3 = 3,666… → 3,67.
        Assert.Equal([3.67m, 3.67m, 3.67m], changes.Take(3).Select(change => change.Average));
        Assert.Equal([9.33m, -3.67m, -5.67m], changes.Take(3).Select(change => change.Difference));
        Assert.Equal([9m, 6m, 6m], changes.Take(3).Select(change => change.NewPrice));

        // Um atacante sozinho é a própria média: diferença zero, preço parado.
        Assert.Equal(0m, changes[3].Difference);
        Assert.Equal(9m, changes[3].NewPrice);

        // Técnicos formam uma posição própria: média 2,65, como no exemplo aprovado.
        Assert.Equal(2.65m, changes[4].Average);
        Assert.Equal(0.35m, changes[4].Difference);
        Assert.Equal(0m, changes[4].Variation);

        // Quem não jogou não valoriza nem desvaloriza.
        Assert.Null(changes[6].Average);
        Assert.Equal(0m, changes[6].Variation);
        Assert.Equal(7m, changes[6].NewPrice);
    }

    [Fact]
    public void PriceNeverLeavesTheFloorOrTheCeiling()
    {
        ValuationInput[] assets =
        [
            Defender(20m, price: 29.5m),
            Defender(-20m, price: 1.5m),
            Defender(0m, price: 7m),
        ];

        var changes = Valuation.Apply(Fut7, assets);

        Assert.Equal(30m, changes[0].NewPrice);
        Assert.Equal(1m, changes[1].NewPrice);
    }

    private static ValuationInput Defender(decimal points, bool played = true, decimal price = 7m) =>
        new(AssetKind.Athlete, Guid.NewGuid(), Position.Defender, played, points, price);

    /// <summary>Decimal escrito com ponto: atributos não aceitam literais `decimal`.</summary>
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
