namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Campeonato de uma organização. Nasce em rascunho, guarda a versão do perfil da
/// modalidade com que foi criado e só troca de modalidade enquanto não é publicado.
/// </summary>
public sealed class Competition
{
    private Competition()
    {
    }

    private Competition(
        Guid id,
        Guid organizationId,
        Guid createdByUserId,
        CompetitionSettings settings,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = CompetitionStatus.Draft;
        Apply(settings);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Season { get; private set; } = string.Empty;

    public Modality Modality { get; private set; }

    /// <summary>Versão de <see cref="ModalityProfiles"/> que vale para este campeonato.</summary>
    public int ModalityProfileVersion { get; private set; }

    /// <summary>Fuso IANA usado para exibir horários e contar dias úteis.</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    /// <summary>Quanto antes da primeira partida da rodada o mercado fecha.</summary>
    public TimeSpan MarketCloseLeadTime { get; private set; }

    public int ResultsSlaBusinessDays { get; private set; }

    public int CorrectionWindowBusinessDays { get; private set; }

    public CompetitionStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ModalityProfile ModalityProfile =>
        ModalityProfiles.Find(Modality, ModalityProfileVersion)
        ?? throw new InvalidOperationException("O campeonato aponta para um perfil de modalidade inexistente.");

    /// <summary>A modalidade define formação e orçamento; depois de publicar, não muda mais.</summary>
    public bool CanChangeModality => Status == CompetitionStatus.Draft;

    public static Competition CreateDraft(
        Guid id,
        Guid organizationId,
        Guid createdByUserId,
        CompetitionSettings settings,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (id == Guid.Empty || organizationId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Campeonato, organização e responsável precisam ser identificados.");
        }

        return new Competition(id, organizationId, createdByUserId, settings, createdAt);
    }

    public void UpdateSettings(CompetitionSettings settings, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Modality != Modality && !CanChangeModality)
        {
            throw new InvalidOperationException("A modalidade não muda depois da publicação.");
        }

        Apply(settings);
        UpdatedAt = updatedAt;
    }

    private void Apply(CompetitionSettings settings)
    {
        if (settings.Validate() is [var first, ..])
        {
            throw new ArgumentException(first.Message, nameof(settings));
        }

        var normalized = settings.Normalized();

        // Trocar a modalidade no rascunho adota o perfil vigente; manter preserva a versão.
        if (normalized.Modality != Modality || ModalityProfileVersion == 0)
        {
            ModalityProfileVersion = ModalityProfiles.CurrentFor(normalized.Modality).Version;
        }

        Name = normalized.Name;
        Season = normalized.Season;
        Modality = normalized.Modality;
        TimeZoneId = normalized.TimeZoneId;
        MarketCloseLeadTime = normalized.MarketCloseLeadTime;
        ResultsSlaBusinessDays = normalized.ResultsSlaBusinessDays;
        CorrectionWindowBusinessDays = normalized.CorrectionWindowBusinessDays;
    }
}
