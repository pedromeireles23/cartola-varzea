using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.SportsCatalog;

public sealed class AthleteTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AthleteAndRegistrationNormalizeAndPreserveTheirScope()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var definition = new AthleteDefinition(
            "  Bia  ", Position.Midfielder, PriceTier.Star, null);
        var athlete = Athlete.Create(Guid.NewGuid(), competitionId, definition, Now);
        var registration = RosterRegistration.Create(
            Guid.NewGuid(), competitionId, athlete.Id, teamId, definition, Now);

        Assert.Equal("Bia", athlete.SportingName);
        Assert.Equal(Position.Midfielder, athlete.Position);
        Assert.Equal(competitionId, registration.CompetitionId);
        Assert.Equal(teamId, registration.RealTeamId);
        Assert.Equal(PriceTier.Star, registration.PriceTier);
        Assert.True(registration.IsActive);
    }

    [Theory]
    [InlineData(Position.Goalkeeper, PriceTier.Basic, 5)]
    [InlineData(Position.Defender, PriceTier.Regular, 7)]
    [InlineData(Position.Midfielder, PriceTier.Star, 11)]
    [InlineData(Position.Forward, PriceTier.Star, 12)]
    public void ModalityProfileResolvesRoundedTierPrice(
        Position position,
        PriceTier tier,
        decimal expected)
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);

        Assert.Equal(expected, profile.InitialAthletePrice(position, tier, null));
    }

    [Fact]
    public void ExactPriceOverridesTierWithinLimits()
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Futsal);

        Assert.Equal(13.25m, profile.InitialAthletePrice(Position.Forward, PriceTier.Basic, 13.25m));
        Assert.Empty(new AthleteDefinition(
            "Caio", Position.Forward, PriceTier.Basic, 13.25m).Validate());
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(30.01)]
    [InlineData(7.123)]
    public void InvalidExactPriceIsRefused(decimal price)
    {
        var definition = new AthleteDefinition(
            "Caio", Position.Forward, PriceTier.Regular, price);

        Assert.Contains(
            definition.Validate(),
            error => error.Field == nameof(AthleteDefinition.InitialPriceOverride));
    }

    [Fact]
    public void RegistrationCannotTransferAndReleasePreservesPrice()
    {
        var definition = new AthleteDefinition(
            "Duda", Position.Defender, PriceTier.Star, 14m);
        var registration = RosterRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), definition, Now);

        Assert.Throws<InvalidOperationException>(() => registration.EnsureTeam(Guid.NewGuid()));
        registration.Release(Now.AddHours(1));

        Assert.False(registration.IsActive);
        Assert.Equal(PriceTier.Star, registration.PriceTier);
        Assert.Equal(14m, registration.InitialPriceOverride);
        Assert.Throws<InvalidOperationException>(() => registration.Release(Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => registration.UpdatePricing(
            definition with { PriceTier = PriceTier.Basic }, null));
    }

    [Fact]
    public void PositionLocksAfterFirstMarketAvailability()
    {
        var athlete = Athlete.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new AthleteDefinition("Eli", Position.Goalkeeper, PriceTier.Regular, null),
            Now);
        athlete.MarkMarketAvailable(Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => athlete.Update(
            new AthleteDefinition("Eli", Position.Forward, PriceTier.Regular, null),
            Now.AddHours(2)));

        athlete.Update(
            new AthleteDefinition("Eli Silva", Position.Goalkeeper, PriceTier.Regular, null),
            Now.AddHours(2));
        Assert.Equal("Eli Silva", athlete.SportingName);
    }

    [Fact]
    public void PriceLocksWithThePositionAndOnlyThePriceIsCompared()
    {
        var definition = new AthleteDefinition("Fábio", Position.Forward, PriceTier.Regular, null);
        var registration = RosterRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), definition, Now);

        registration.UpdatePricing(definition with { PriceTier = PriceTier.Star }, null);
        Assert.Equal(PriceTier.Star, registration.PriceTier);

        var lockedSince = Now.AddHours(1);
        Assert.Throws<InvalidOperationException>(() => registration.UpdatePricing(
            definition with { PriceTier = PriceTier.Star, InitialPriceOverride = 9m }, lockedSince));
        Assert.Throws<InvalidOperationException>(() => registration.UpdatePricing(
            definition with { PriceTier = PriceTier.Basic }, lockedSince));

        // Renomear continua livre: o nome não está no preço.
        registration.UpdatePricing(
            definition with { SportingName = "Fábio Lima", PriceTier = PriceTier.Star }, lockedSince);
        Assert.Equal(PriceTier.Star, registration.PriceTier);
        Assert.Null(registration.InitialPriceOverride);
    }
}
