using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class MatchSheetService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IMatchSheetService
{
    public async Task<MatchSheetView?> GetAsync(
        Guid competitionId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var context = await ContextAsync(competitionId, matchId, cancellationToken).ConfigureAwait(false);
        return context is null
            ? null
            : await ViewAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MatchSheetCommandResult> SaveAsync(
        Guid competitionId,
        Guid matchId,
        MatchSheetInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var context = await ContextAsync(competitionId, matchId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.NotFound);
        }

        var now = clock.GetUtcNow();
        if (context.Match.Status != MatchStatus.Scheduled
            || now < context.Match.KickoffAt
            || context.Round.PhaseAt(now) is not (RoundPhase.InProgress or RoundPhase.UnderReview))
        {
            return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.StatusLocked);
        }

        var roster = await RosterAsync(context, cancellationToken).ConfigureAwait(false);
        var rosterByAthlete = roster.ToDictionary(item => item.AthleteId);
        var definitions = new List<MatchSheetAppearanceDefinition>();
        var errors = new List<MatchSheetError>();

        foreach (var appearance in input.Appearances)
        {
            if (!rosterByAthlete.TryGetValue(appearance.AthleteId, out var athlete))
            {
                errors.Add(new(nameof(appearance.AthleteId), "O atleta não pertence ao elenco atual da partida."));
                continue;
            }

            RedCardReason? redCardReason = null;
            if (!string.IsNullOrWhiteSpace(appearance.RedCardReason)
                && (!Enum.TryParse(appearance.RedCardReason, ignoreCase: true, out RedCardReason parsed)
                    || !Enum.IsDefined(parsed)))
            {
                errors.Add(new(nameof(appearance.RedCardReason), "Escolha expulsão direta ou por segundo amarelo."));
            }
            else if (!string.IsNullOrWhiteSpace(appearance.RedCardReason))
            {
                redCardReason = Enum.Parse<RedCardReason>(appearance.RedCardReason, ignoreCase: true);
            }

            definitions.Add(new(
                appearance.AthleteId,
                athlete.RealTeamId,
                athlete.Position,
                appearance.DidPlay,
                appearance.PlayedAsGoalkeeper,
                appearance.GoalsConceded,
                appearance.Goals,
                appearance.Assists,
                appearance.GoalkeeperSaves,
                appearance.PenaltySaves,
                appearance.YellowCards,
                appearance.RedCards,
                redCardReason,
                appearance.OwnGoals,
                appearance.PenaltyMisses));
        }

        var submittedAthletes = input.Appearances.Select(item => item.AthleteId).ToHashSet();
        if (submittedAthletes.Count != roster.Count
            || roster.Any(item => !submittedAthletes.Contains(item.AthleteId)))
        {
            errors.Add(new(
                nameof(input.Appearances),
                "Informe a participação de todos os atletas elegíveis para a partida."));
        }

        NormalizeSingleGoalkeeper(definitions, context.Match.HomeTeamId, input.AwayScore);
        NormalizeSingleGoalkeeper(definitions, context.Match.AwayTeamId, input.HomeScore);
        var definition = new MatchSheetDefinition(input.HomeScore, input.AwayScore, definitions);
        errors.AddRange(definition.Validate(context.Match.HomeTeamId, context.Match.AwayTeamId));
        if (errors.Count > 0)
        {
            return new(MatchSheetCommandOutcome.Invalid, null, errors);
        }

        var sheet = await dbContext.MatchSheets
            .SingleOrDefaultAsync(
                item => item.CompetitionId == competitionId && item.MatchId == matchId,
                cancellationToken)
            .ConfigureAwait(false);
        if (sheet is null)
        {
            if (!string.IsNullOrWhiteSpace(input.Version))
            {
                return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.Conflict);
            }

            sheet = MatchSheet.Create(
                Guid.CreateVersion7(), competitionId, matchId, input.HomeScore, input.AwayScore, now);
            dbContext.MatchSheets.Add(sheet);
        }
        else
        {
            if (!RowVersions.Matches(input.Version, sheet.RowVersion, out var expectedVersion))
            {
                return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.Conflict);
            }

            dbContext.Entry(sheet).Property(item => item.RowVersion).OriginalValue = expectedVersion;
            sheet.UpdateScore(input.HomeScore, input.AwayScore, now);
            await ReplaceDetailsAsync(sheet, cancellationToken).ConfigureAwait(false);
        }

        AddDetails(sheet, definitions);
        AddAudit(sheet, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.Conflict);
        }
        catch (DbUpdateException)
        {
            // Duas primeiras gravações podem disputar o índice único da partida.
            return MatchSheetCommandResult.Of(MatchSheetCommandOutcome.Conflict);
        }

        var view = await ViewAsync(context, cancellationToken).ConfigureAwait(false);
        return new(MatchSheetCommandOutcome.Completed, view, []);
    }

    private async Task<MatchContext?> ContextAsync(
        Guid competitionId,
        Guid matchId,
        CancellationToken cancellationToken) =>
        await (
            from match in dbContext.Matches
            join round in dbContext.Rounds on match.RoundId equals round.Id
            join home in dbContext.RealTeams on match.HomeTeamId equals home.Id
            join away in dbContext.RealTeams on match.AwayTeamId equals away.Id
            where match.CompetitionId == competitionId
                && round.CompetitionId == competitionId
                && match.Id == matchId
            select new MatchContext(match, round, home.Name, away.Name))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<List<RosterRow>> RosterAsync(
        MatchContext context,
        CancellationToken cancellationToken) =>
        await (
            from registration in dbContext.RosterRegistrations.AsNoTracking()
            join athlete in dbContext.Athletes.AsNoTracking() on registration.AthleteId equals athlete.Id
            where registration.CompetitionId == context.Match.CompetitionId
                && athlete.CompetitionId == context.Match.CompetitionId
                && registration.RegisteredAt <= context.Match.KickoffAt
                && (registration.ReleasedAt == null || registration.ReleasedAt >= context.Match.KickoffAt)
                && (registration.RealTeamId == context.Match.HomeTeamId
                    || registration.RealTeamId == context.Match.AwayTeamId)
            orderby registration.RealTeamId, athlete.SportingName
            select new RosterRow(
                athlete.Id,
                registration.RealTeamId,
                athlete.SportingName,
                athlete.Position))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<MatchSheetView> ViewAsync(
        MatchContext context,
        CancellationToken cancellationToken)
    {
        var roster = await RosterAsync(context, cancellationToken).ConfigureAwait(false);
        var sheet = await dbContext.MatchSheets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CompetitionId == context.Match.CompetitionId
                    && item.MatchId == context.Match.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var appearances = sheet is null
            ? []
            : await dbContext.AthleteAppearances
                .AsNoTracking()
                .Where(item => item.CompetitionId == context.Match.CompetitionId
                    && item.MatchSheetId == sheet.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var events = sheet is null
            ? []
            : await dbContext.StatEvents
                .AsNoTracking()
                .Where(item => item.CompetitionId == context.Match.CompetitionId
                    && item.MatchSheetId == sheet.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var appearancesByAthlete = appearances.ToDictionary(item => item.AthleteId);
        var eventsByAthlete = events
            .GroupBy(item => item.AthleteId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.Type));

        return new(
            context.Match.Id,
            context.Round.Id,
            context.Round.Name,
            context.Round.PhaseAt(clock.GetUtcNow()).ToString(),
            context.Match.KickoffAt,
            context.Match.HomeTeamId,
            context.HomeTeamName,
            context.Match.AwayTeamId,
            context.AwayTeamName,
            sheet?.HomeScore ?? 0,
            sheet?.AwayScore ?? 0,
            [.. roster.Select(athlete => AthleteView(
                athlete,
                appearancesByAthlete.GetValueOrDefault(athlete.AthleteId),
                eventsByAthlete.GetValueOrDefault(athlete.AthleteId)))],
            sheet is null ? null : Convert.ToBase64String(sheet.RowVersion));
    }

    private static MatchSheetAthleteView AthleteView(
        RosterRow athlete,
        AthleteAppearance? appearance,
        IReadOnlyDictionary<StatEventType, StatEvent>? events)
    {
        var redCard = events?.GetValueOrDefault(StatEventType.RedCard);
        return new(
            athlete.AthleteId,
            athlete.RealTeamId,
            athlete.SportingName,
            athlete.Position.ToString(),
            appearance?.DidPlay ?? false,
            appearance?.PlayedAsGoalkeeper ?? false,
            appearance?.GoalsConceded ?? 0,
            Quantity(events, StatEventType.Goal),
            Quantity(events, StatEventType.Assist),
            Quantity(events, StatEventType.GoalkeeperSave),
            Quantity(events, StatEventType.PenaltySave),
            Quantity(events, StatEventType.YellowCard),
            redCard?.Quantity ?? 0,
            redCard?.RedCardReason?.ToString(),
            Quantity(events, StatEventType.OwnGoal),
            Quantity(events, StatEventType.PenaltyMiss));
    }

    private static int Quantity(
        IReadOnlyDictionary<StatEventType, StatEvent>? events,
        StatEventType type) => events?.GetValueOrDefault(type)?.Quantity ?? 0;

    private static void NormalizeSingleGoalkeeper(
        List<MatchSheetAppearanceDefinition> definitions,
        Guid teamId,
        int goalsConceded)
    {
        var indexes = definitions
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.RealTeamId == teamId
                && pair.item.DidPlay
                && pair.item.PlayedAsGoalkeeper)
            .Select(pair => pair.index)
            .ToArray();
        if (indexes.Length == 1)
        {
            var index = indexes[0];
            definitions[index] = definitions[index] with { GoalsConceded = goalsConceded };
        }
    }

    private async Task ReplaceDetailsAsync(MatchSheet sheet, CancellationToken cancellationToken)
    {
        var appearances = await dbContext.AthleteAppearances
            .Where(item => item.CompetitionId == sheet.CompetitionId && item.MatchSheetId == sheet.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var events = await dbContext.StatEvents
            .Where(item => item.CompetitionId == sheet.CompetitionId && item.MatchSheetId == sheet.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.AthleteAppearances.RemoveRange(appearances);
        dbContext.StatEvents.RemoveRange(events);
    }

    private void AddDetails(MatchSheet sheet, IEnumerable<MatchSheetAppearanceDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            dbContext.AthleteAppearances.Add(AthleteAppearance.Create(
                Guid.CreateVersion7(), sheet.CompetitionId, sheet.Id, definition));
            foreach (var statEvent in definition.Events())
            {
                dbContext.StatEvents.Add(StatEvent.Create(
                    Guid.CreateVersion7(), sheet.CompetitionId, sheet.Id, definition.AthleteId, statEvent));
            }
        }
    }

    private void AddAudit(MatchSheet sheet, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            "CompetitionMatchSheetSaved",
            sheet.Id,
            "Súmula da partida salva.",
            occurredAt));

    private sealed record MatchContext(
        Match Match,
        Round Round,
        string HomeTeamName,
        string AwayTeamName);

    private sealed record RosterRow(
        Guid AthleteId,
        Guid RealTeamId,
        string SportingName,
        Position Position);
}
