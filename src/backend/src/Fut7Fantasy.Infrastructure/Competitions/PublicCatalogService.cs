using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Fut7Fantasy.Infrastructure.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

/// <summary>
/// Time e atleta públicos (Fase 8).
///
/// Estatística e histórico de preço saem só de rodada publicada, pela mesma razão do
/// calendário: em conferência a súmula está sendo escrita. E de uma pessoa sai só o que
/// o 01 §11 permite — nome esportivo, posição, time, preço e o que ela fez em campo.
/// </summary>
public sealed class PublicCatalogService(Fut7FantasyDbContext dbContext) : IPublicCatalogService
{
    public async Task<PublicTeamDetailView?> TeamAsync(
        string slug,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var competition = await PublishedAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var team = await dbContext.RealTeams
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == teamId && item.CompetitionId == competition.Id,
                cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return null;
        }

        var elenco = await (
                from registration in dbContext.RosterRegistrations.AsNoTracking()
                where registration.RealTeamId == teamId
                join athlete in dbContext.Athletes.AsNoTracking()
                    on registration.AthleteId equals athlete.Id
                select new { Athlete = athlete, Registration = registration })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // Sem nome informado, o técnico é "Técnico do {time}" (01 §7): o organizador não
        // é obrigado a nomear quem talvez nem exista como pessoa identificada.
        var coach = await dbContext.Coaches
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.RealTeamId == teamId, cancellationToken)
            .ConfigureAwait(false);

        var prices = await AssetPrices.CurrentAsync(dbContext, competition.Id, cancellationToken)
            .ConfigureAwait(false);
        var profile = ModalityProfiles.CurrentFor(competition.Modality);

        return new(
            team.Id,
            competition.Slug!,
            competition.Name,
            team.Name,
            coach?.EffectiveName(team.Name),
            [
                .. elenco
                    .Select(item => new PublicSquadAthleteView(
                        item.Athlete.Id,
                        item.Athlete.SportingName,
                        item.Athlete.Position.ToString(),
                        Price(prices, profile, item.Athlete, item.Registration),
                        item.Registration.IsActive))
                    // Quem está no elenco primeiro; depois por posição e nome, que é a
                    // ordem em que uma escalação se lê.
                    .OrderByDescending(atleta => atleta.Active)
                    .ThenBy(atleta => PositionOrder(atleta.Position))
                    .ThenBy(atleta => atleta.SportingName, StringComparer.Ordinal),
            ]);
    }

    public async Task<PublicAthleteView?> AthleteAsync(
        string slug,
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var competition = await PublishedAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var found = await (
                from athlete in dbContext.Athletes.AsNoTracking()
                where athlete.Id == athleteId && athlete.CompetitionId == competition.Id
                join registration in dbContext.RosterRegistrations.AsNoTracking()
                    on athlete.Id equals registration.AthleteId
                join team in dbContext.RealTeams.AsNoTracking()
                    on registration.RealTeamId equals team.Id
                select new { Athlete = athlete, Registration = registration, Team = team })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var prices = await AssetPrices.CurrentAsync(dbContext, competition.Id, cancellationToken)
            .ConfigureAwait(false);
        var profile = ModalityProfiles.CurrentFor(competition.Modality);

        return new(
            found.Athlete.Id,
            competition.Slug!,
            competition.Name,
            found.Athlete.SportingName,
            found.Athlete.Position.ToString(),
            found.Team.Id,
            found.Team.Name,
            Price(prices, profile, found.Athlete, found.Registration),
            found.Registration.IsActive,
            await TotalsAsync(competition.Id, athleteId, cancellationToken).ConfigureAwait(false),
            await PriceHistoryAsync(competition.Id, athleteId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Só rodada publicada conta: em conferência a súmula ainda está sendo escrita.</summary>
    private async Task<PublicAthleteTotalsView> TotalsAsync(
        Guid competitionId,
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var sheets = await (
                from sheet in dbContext.MatchSheets.AsNoTracking()
                where sheet.CompetitionId == competitionId
                join match in dbContext.Matches.AsNoTracking() on sheet.MatchId equals match.Id
                join round in dbContext.Rounds.AsNoTracking() on match.RoundId equals round.Id
                where round.Status == RoundStatus.Published
                select sheet.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sheets.Count == 0)
        {
            return new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var appearances = await dbContext.AthleteAppearances
            .AsNoTracking()
            .Where(item => item.AthleteId == athleteId && sheets.Contains(item.MatchSheetId) && item.DidPlay)
            .Select(item => new { item.PlayedAsGoalkeeper, item.GoalsConceded })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var events = await dbContext.StatEvents
            .AsNoTracking()
            .Where(item => item.AthleteId == athleteId && sheets.Contains(item.MatchSheetId))
            .Select(item => new { item.Type, item.Quantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int Total(StatEventType type) =>
            events.Where(item => item.Type == type).Sum(item => item.Quantity);

        return new(
            appearances.Count,
            Total(StatEventType.Goal),
            Total(StatEventType.Assist),
            Total(StatEventType.GoalkeeperSave),
            Total(StatEventType.PenaltySave),
            Total(StatEventType.YellowCard),
            Total(StatEventType.RedCard),
            Total(StatEventType.OwnGoal),
            Total(StatEventType.PenaltyMiss),
            appearances.Count(item => item.PlayedAsGoalkeeper && item.GoalsConceded == 0));
    }

    /// <summary>
    /// O preço rodada a rodada, na revisão que vale agora: uma correção republicada
    /// substitui a anterior, e o histórico precisa contar a versão vigente.
    /// </summary>
    private async Task<IReadOnlyList<PublicAthletePriceView>> PriceHistoryAsync(
        Guid competitionId,
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var calculations = await (
                from calculation in dbContext.RoundCalculations.AsNoTracking()
                where calculation.CompetitionId == competitionId
                join round in dbContext.Rounds.AsNoTracking() on calculation.RoundId equals round.Id
                where round.Status == RoundStatus.Published
                select new
                {
                    calculation.Id,
                    calculation.Revision,
                    round.Name,
                    round.Sequence,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (calculations.Count == 0)
        {
            return [];
        }

        var vigentes = calculations
            .GroupBy(item => item.Sequence)
            .Select(group => group.MaxBy(item => item.Revision)!)
            .ToDictionary(item => item.Id);
        var ids = vigentes.Keys.ToList();

        var changes = await dbContext.AssetPriceChanges
            .AsNoTracking()
            .Where(change =>
                change.AssetId == athleteId
                && change.Kind == AssetKind.Athlete
                && ids.Contains(change.CalculationId))
            .Select(change => new
            {
                change.CalculationId,
                change.PreviousPrice,
                change.NewPrice,
                change.Variation,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. changes
                .Select(change =>
                {
                    var rodada = vigentes[change.CalculationId];
                    return new PublicAthletePriceView(
                        rodada.Name,
                        rodada.Sequence,
                        change.PreviousPrice,
                        change.NewPrice,
                        change.Variation);
                })
                .OrderBy(item => item.Sequence),
        ];
    }

    private static decimal Price(
        IReadOnlyDictionary<(AssetKind Kind, Guid Id), decimal> prices,
        ModalityProfile profile,
        Athlete athlete,
        RosterRegistration registration) =>
        prices.TryGetValue((AssetKind.Athlete, athlete.Id), out var price)
            ? price
            : profile.InitialAthletePrice(
                athlete.Position,
                registration.PriceTier,
                registration.InitialPriceOverride);

    /// <summary>Do gol para o ataque, que é como um elenco se lê.</summary>
    private static int PositionOrder(string position) => position switch
    {
        nameof(Position.Goalkeeper) => 0,
        nameof(Position.Defender) => 1,
        nameof(Position.Midfielder) => 2,
        _ => 3,
    };

    private Task<Competition?> PublishedAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Slug == slug && item.Status == CompetitionStatus.Published,
                cancellationToken);
}
