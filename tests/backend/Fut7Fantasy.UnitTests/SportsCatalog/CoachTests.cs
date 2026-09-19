using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.SportsCatalog;

public sealed class CoachTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CoachWithoutPersonUsesStableTeamFallback()
    {
        var teamId = Guid.NewGuid();
        var coach = Coach.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            teamId,
            new CoachDefinition(null, PriceTier.Regular, null),
            Now);

        Assert.Equal(teamId, coach.RealTeamId);
        Assert.Null(coach.DisplayName);
        Assert.Equal("Técnico do União da Vila", coach.EffectiveName("União da Vila"));
        Assert.Equal(PriceTier.Regular, coach.PriceTier);
    }

    [Fact]
    public void BlankNameNormalizesBackToFallback()
    {
        var coach = Coach.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CoachDefinition("Professor Beto", PriceTier.Star, 12.5m),
            Now);

        coach.Update(new CoachDefinition("   ", PriceTier.Basic, null), Now.AddHours(1));

        Assert.Null(coach.DisplayName);
        Assert.Equal("Técnico do Estrela", coach.EffectiveName("Estrela"));
        Assert.Equal(PriceTier.Basic, coach.PriceTier);
        Assert.Null(coach.InitialPriceOverride);
    }

    [Fact]
    public void PriceLocksAtFirstMarketAvailabilityButTheNameStaysEditable()
    {
        var coach = Coach.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CoachDefinition(null, PriceTier.Regular, null),
            Now);
        coach.MarkMarketAvailable(Now.AddHours(1));
        coach.MarkMarketAvailable(Now.AddHours(5));

        Assert.Equal(Now.AddHours(1), coach.FirstMarketAvailableAt);
        Assert.Throws<InvalidOperationException>(() => coach.Update(
            new CoachDefinition(null, PriceTier.Star, null), Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => coach.Update(
            new CoachDefinition(null, PriceTier.Regular, 9m), Now.AddHours(2)));

        coach.Update(new CoachDefinition("Professora Ana", PriceTier.Regular, null), Now.AddHours(2));
        Assert.Equal("Professora Ana", coach.DisplayName);
        Assert.Equal(PriceTier.Regular, coach.PriceTier);
    }

    [Fact]
    public void ShortPersonNameAndInvalidPriceAreRefused()
    {
        var definition = new CoachDefinition("x", PriceTier.Regular, 30.01m);

        Assert.Contains(
            definition.Validate(),
            error => error.Field == nameof(CoachDefinition.DisplayName));
        Assert.Contains(
            definition.Validate(),
            error => error.Field == nameof(CoachDefinition.InitialPriceOverride));
        Assert.Throws<ArgumentException>(() => Coach.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), definition, Now));
    }

    [Theory]
    [InlineData(PriceTier.Basic, 5.5)]
    [InlineData(PriceTier.Regular, 8)]
    [InlineData(PriceTier.Star, 11)]
    public void ModalityProfileResolvesCoachTierPrice(PriceTier tier, decimal expected)
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);

        Assert.Equal(expected, profile.InitialCoachPrice(tier, null));
    }

    [Fact]
    public void ExactCoachPriceOverridesTier()
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Field);

        Assert.Equal(13.25m, profile.InitialCoachPrice(PriceTier.Basic, 13.25m));
    }
}
