using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.UnitTests.Competitions;

public sealed class CompetitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewCompetitionStartsAsDraftWithTheCurrentModalityProfile()
    {
        var competition = CreateDraft(Settings() with { Name = "  Copa da Várzea  ", Season = " 2026 " });

        Assert.Equal(CompetitionStatus.Draft, competition.Status);
        Assert.Equal("Copa da Várzea", competition.Name);
        Assert.Equal("2026", competition.Season);
        Assert.Equal(ModalityProfiles.CurrentFor(Modality.Fut7).Version, competition.ModalityProfileVersion);
        Assert.Same(ModalityProfiles.CurrentFor(Modality.Fut7), competition.ModalityProfile);
        Assert.True(competition.CanChangeModality);
        Assert.Equal(Now, competition.CreatedAt);
        Assert.Equal(Now, competition.UpdatedAt);
    }

    [Fact]
    public void DraftCanChangeModality()
    {
        var competition = CreateDraft(Settings());
        var later = Now.AddHours(1);

        competition.UpdateSettings(Settings() with { Modality = Modality.Field }, later);

        Assert.Equal(Modality.Field, competition.Modality);
        Assert.Equal(135m, competition.ModalityProfile.Budget);
        Assert.Equal(later, competition.UpdatedAt);
    }

    [Fact]
    public void DefaultsFollowTheProductRules()
    {
        Assert.Equal("America/Sao_Paulo", CompetitionSettings.DefaultTimeZoneId);
        Assert.Equal(2, CompetitionSettings.DefaultResultsSlaBusinessDays);
        Assert.Equal(3, CompetitionSettings.DefaultCorrectionWindowBusinessDays);
    }

    [Fact]
    public void ValidSettingsHaveNoErrors()
    {
        Assert.Empty(Settings().Validate());
    }

    [Theory]
    [InlineData("America/Sao_Paulo")]
    [InlineData("America/Manaus")]
    [InlineData("America/Noronha")]
    [InlineData("UTC")]
    public void IanaTimeZonesAreAccepted(string timeZoneId)
    {
        Assert.Empty((Settings() with { TimeZoneId = timeZoneId }).Validate());
    }

    [Theory]
    [InlineData("E. South America Standard Time")]
    [InlineData("America/Cidade_Inventada")]
    [InlineData("")]
    public void TimeZonesOutsideIanaAreRejected(string timeZoneId)
    {
        var errors = (Settings() with { TimeZoneId = timeZoneId }).Validate();

        Assert.Equal(nameof(CompetitionSettings.TimeZoneId), Assert.Single(errors).Field);
    }

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void InvalidSettingsPointToTheFieldAndAreRefusedByTheAggregate(
        CompetitionSettings settings,
        string field)
    {
        Assert.Equal(field, Assert.Single(settings.Validate()).Field);
        Assert.Throws<ArgumentException>(() => CreateDraft(settings));

        var competition = CreateDraft(Settings());
        Assert.Throws<ArgumentException>(() => competition.UpdateSettings(settings, Now));
        Assert.Equal("Copa de Teste", competition.Name);
    }

    public static TheoryData<CompetitionSettings, string> InvalidSettings() => new()
    {
        { Settings() with { Name = "ab" }, nameof(CompetitionSettings.Name) },
        { Settings() with { Name = "   abc   " + new string('x', 118) }, nameof(CompetitionSettings.Name) },
        { Settings() with { Season = "  " }, nameof(CompetitionSettings.Season) },
        { Settings() with { Season = new string('9', 41) }, nameof(CompetitionSettings.Season) },
        { Settings() with { Modality = (Modality)99 }, nameof(CompetitionSettings.Modality) },
        {
            Settings() with { MarketCloseLeadTime = TimeSpan.FromMinutes(-1) },
            nameof(CompetitionSettings.MarketCloseLeadTime)
        },
        {
            Settings() with { MarketCloseLeadTime = TimeSpan.FromHours(72) + TimeSpan.FromMinutes(1) },
            nameof(CompetitionSettings.MarketCloseLeadTime)
        },
        {
            Settings() with { MarketCloseLeadTime = TimeSpan.FromSeconds(90) },
            nameof(CompetitionSettings.MarketCloseLeadTime)
        },
        { Settings() with { ResultsSlaBusinessDays = 0 }, nameof(CompetitionSettings.ResultsSlaBusinessDays) },
        { Settings() with { ResultsSlaBusinessDays = 11 }, nameof(CompetitionSettings.ResultsSlaBusinessDays) },
        {
            Settings() with { CorrectionWindowBusinessDays = 0 },
            nameof(CompetitionSettings.CorrectionWindowBusinessDays)
        },
        {
            Settings() with { CorrectionWindowBusinessDays = 11 },
            nameof(CompetitionSettings.CorrectionWindowBusinessDays)
        },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(72 * 60)]
    public void MarketCloseLeadTimeLimitsAreInclusive(int minutes)
    {
        Assert.Empty((Settings() with { MarketCloseLeadTime = TimeSpan.FromMinutes(minutes) }).Validate());
    }

    [Fact]
    public void DraftNeedsIdentifiedOrganizationAndAuthor()
    {
        Assert.Throws<ArgumentException>(() =>
            Competition.CreateDraft(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Settings(), Now));
        Assert.Throws<ArgumentException>(() =>
            Competition.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Settings(), Now));
    }

    [Fact]
    public void PublishingMakesTheCompetitionPublicAndKeepsTheFirstInstant()
    {
        var competition = CreateDraft(Settings());
        var published = Now.AddHours(1);

        competition.Publish(published, "copa-de-teste-2026");

        Assert.Equal(CompetitionStatus.Published, competition.Status);
        Assert.True(competition.IsPublished);
        Assert.Equal(published, competition.PublishedAt);
        Assert.Equal(published, competition.UpdatedAt);

        competition.Unpublish(published.AddDays(1));
        competition.Publish(published.AddDays(2));

        Assert.Equal(published, competition.PublishedAt);

        // O endereço é dado uma vez: republicar não o renegocia.
        Assert.Equal("copa-de-teste-2026", competition.Slug);
    }

    [Fact]
    public void ModalityStaysLockedAfterTheFirstPublication()
    {
        var competition = CreateDraft(Settings());
        competition.Publish(Now.AddHours(1), "copa-de-teste-2026");
        competition.Unpublish(Now.AddHours(2));

        // Voltar para rascunho não devolve a modalidade: o público já viu as regras antigas.
        Assert.Equal(CompetitionStatus.Draft, competition.Status);
        Assert.False(competition.CanChangeModality);
        Assert.Throws<InvalidOperationException>(() =>
            competition.UpdateSettings(Settings() with { Modality = Modality.Futsal }, Now.AddHours(3)));

        // O resto da configuração continua editável no rascunho.
        competition.UpdateSettings(Settings() with { Season = "2027" }, Now.AddHours(3));
        Assert.Equal("2027", competition.Season);
    }

    [Fact]
    public void PublicationTransitionsRefuseTheStateTheyAreAlreadyIn()
    {
        var competition = CreateDraft(Settings());

        Assert.Throws<InvalidOperationException>(() => competition.Unpublish(Now));

        competition.Publish(Now, "copa-de-teste-2026");
        Assert.Throws<InvalidOperationException>(() => competition.Publish(Now));
    }

    [Fact]
    public void FirstPublicationRefusesAnAddressThatWouldNotWorkInAUrl()
    {
        var competition = CreateDraft(Settings());

        Assert.Throws<ArgumentException>(() => competition.Publish(Now, null));
        Assert.Throws<ArgumentException>(() => competition.Publish(Now, "Copa de Teste"));
        Assert.Equal(CompetitionStatus.Draft, competition.Status);
        Assert.Null(competition.Slug);
    }

    [Fact]
    public void RenamingAfterPublicationKeepsTheAddressAlreadyShared()
    {
        var competition = CreateDraft(Settings());
        competition.Publish(Now, "copa-de-teste-2026");

        competition.Unpublish(Now.AddHours(1));
        competition.UpdateSettings(Settings() with { Name = "Outro Nome" }, Now.AddHours(2));
        competition.Publish(Now.AddHours(3));

        Assert.Equal("copa-de-teste-2026", competition.Slug);
    }

    private static Competition CreateDraft(CompetitionSettings settings) =>
        Competition.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), settings, Now);

    private static CompetitionSettings Settings() => new(
        "Copa de Teste",
        "2026",
        Modality.Fut7,
        CompetitionSettings.DefaultTimeZoneId,
        TimeSpan.FromMinutes(30),
        CompetitionSettings.DefaultResultsSlaBusinessDays,
        CompetitionSettings.DefaultCorrectionWindowBusinessDays);
}
