using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.UnitTests.Competitions;

/// <summary>Os perfis precisam reproduzir as tabelas aprovadas no 01 §7 e §9.</summary>
public sealed class ModalityProfileTests
{
    [Theory]
    [InlineData(Modality.Fut7, 7, 1, 2, 2, 2, 11, 100)]
    [InlineData(Modality.Futsal, 5, 1, 1, 2, 1, 9, 80)]
    [InlineData(Modality.Field, 11, 1, 4, 3, 3, 15, 135)]
    public void CurrentProfileMatchesTheApprovedModalityTable(
        Modality modality,
        int starters,
        int goalkeepers,
        int defenders,
        int midfielders,
        int forwards,
        int squadAthletes,
        int budget)
    {
        var profile = ModalityProfiles.CurrentFor(modality);

        Assert.Equal(1, profile.Version);
        Assert.Equal(starters, profile.Starters);
        Assert.Equal(new Formation(goalkeepers, defenders, midfielders, forwards), profile.Formation);
        Assert.Equal(4, profile.BenchSize);
        Assert.Equal(squadAthletes, profile.SquadAthletes);
        Assert.Equal(budget, profile.Budget);
        Assert.Equal(starters + 2, profile.MinimumAthletesPerRealTeam);
    }

    // Tabela "Valores derivados por modalidade" do 01 §9: máximo entre titulares / no elenco.
    [Theory]
    [InlineData(Modality.Fut7, 4, 3, 5)]
    [InlineData(Modality.Fut7, 3, 4, 6)]
    [InlineData(Modality.Fut7, 2, 5, 8)]
    [InlineData(Modality.Futsal, 4, 2, 4)]
    [InlineData(Modality.Futsal, 3, 3, 5)]
    [InlineData(Modality.Futsal, 2, 4, 7)]
    [InlineData(Modality.Field, 4, 4, 6)]
    [InlineData(Modality.Field, 3, 6, 8)]
    [InlineData(Modality.Field, 2, 8, 11)]
    public void RealTeamLimitMatchesTheApprovedTable(
        Modality modality,
        int activeRealTeams,
        int maxStarters,
        int maxAthletes)
    {
        var limit = ModalityProfiles.CurrentFor(modality).RealTeamLimitFor(activeRealTeams);

        Assert.Equal(new RealTeamLimit(maxStarters, maxAthletes), limit);
    }

    [Theory]
    [InlineData(Modality.Fut7)]
    [InlineData(Modality.Futsal)]
    [InlineData(Modality.Field)]
    public void ManyActiveTeamsUseTheSameLimitAsFour(Modality modality)
    {
        var profile = ModalityProfiles.CurrentFor(modality);

        Assert.Equal(profile.RealTeamLimitFor(4), profile.RealTeamLimitFor(16));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void RoundWithFewerThanTwoActiveTeamsHasNoLimit(int activeRealTeams)
    {
        var profile = ModalityProfiles.CurrentFor(Modality.Fut7);

        Assert.Throws<ArgumentOutOfRangeException>(() => profile.RealTeamLimitFor(activeRealTeams));
    }

    [Theory]
    [InlineData(Modality.Fut7)]
    [InlineData(Modality.Futsal)]
    [InlineData(Modality.Field)]
    public void FallbackPricesAreTheSameInEveryModality(Modality modality)
    {
        var profile = ModalityProfiles.CurrentFor(modality);

        Assert.Equal(7m, profile.FallbackPrices[Position.Goalkeeper]);
        Assert.Equal(7m, profile.FallbackPrices[Position.Defender]);
        Assert.Equal(8m, profile.FallbackPrices[Position.Midfielder]);
        Assert.Equal(9m, profile.FallbackPrices[Position.Forward]);
        Assert.Equal(8m, profile.CoachFallbackPrice);
    }

    [Fact]
    public void CurrentListsOneProfilePerModality()
    {
        Assert.Equal(
            [Modality.Fut7, Modality.Futsal, Modality.Field],
            ModalityProfiles.Current.Select(profile => profile.Modality));
    }

    [Fact]
    public void UnknownVersionIsNotFound()
    {
        Assert.Null(ModalityProfiles.Find(Modality.Fut7, 99));
        Assert.Same(ModalityProfiles.CurrentFor(Modality.Fut7), ModalityProfiles.Find(Modality.Fut7, 1));
    }
}
