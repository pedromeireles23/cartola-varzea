using System.Data;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class CompetitionRoundService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICompetitionRoundService
{
    public async Task<IReadOnlyList<RoundView>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await CompetitionAsync(competitionId, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return [];
        }

        var rounds = await RoundsOf(competitionId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ViewsAsync(competition, rounds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RoundReviewView?> ReviewAsync(
        Guid competitionId,
        Guid roundId,
        CancellationToken cancellationToken)
    {
        var competition = await CompetitionAsync(competitionId, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var round = await RoundsOf(competitionId)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == roundId, cancellationToken)
            .ConfigureAwait(false);
        return round is null
            ? null
            : await BuildReviewAsync(competition, round, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
    }

    public Task<RoundCommandResult> CreateAsync(
        Guid competitionId,
        RoundDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return Task.FromResult(new RoundCommandResult(RoundCommandOutcome.Invalid, null, errors));
        }

        return InCompetitionLockAsync(competitionId, async () =>
        {
            var competition = await CompetitionAsync(competitionId, cancellationToken).ConfigureAwait(false);
            if (competition is null)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.NotFound);
            }

            var count = await dbContext.Rounds
                .CountAsync(round => round.CompetitionId == competitionId, cancellationToken)
                .ConfigureAwait(false);
            if (count >= Round.MaxRoundsPerCompetition)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.LimitReached);
            }

            var now = clock.GetUtcNow();
            var round = Round.Create(Guid.CreateVersion7(), competitionId, count + 1, definition, now);
            dbContext.Rounds.Add(round);
            AddAudit("CompetitionRoundCreated", round.Id, "Rodada criada.", now);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await CompletedAsync(competition, round, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<RoundCommandResult> RenameAsync(
        Guid competitionId,
        Guid roundId,
        RoundDefinition definition,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Validate() is { Count: > 0 } errors)
        {
            return Task.FromResult(new RoundCommandResult(RoundCommandOutcome.Invalid, null, errors));
        }

        return MutateAsync(competitionId, roundId, version, (competition, round, now) =>
        {
            round.Update(definition, now);
            AddAudit("CompetitionRoundUpdated", round.Id, "Rodada alterada.", now);
            return Task.FromResult<RoundCommandResult?>(null);
        }, cancellationToken);
    }

    public Task<RoundCommandOutcome> DeleteAsync(
        Guid competitionId,
        Guid roundId,
        CancellationToken cancellationToken) =>
        InCompetitionLockAsync(competitionId, async () =>
        {
            var rounds = await RoundsOf(competitionId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var removed = rounds.SingleOrDefault(round => round.Id == roundId);
            if (removed is null)
            {
                return RoundCommandOutcome.NotFound;
            }

            if (!removed.AcceptsMatchChanges)
            {
                return RoundCommandOutcome.StatusLocked;
            }

            var matches = await dbContext.Matches
                .Where(match => match.RoundId == roundId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            dbContext.Matches.RemoveRange(matches);
            dbContext.Rounds.Remove(removed);

            var sequence = 1;
            foreach (var round in rounds.Where(round => round.Id != roundId))
            {
                round.MoveTo(sequence++);
            }

            AddAudit("CompetitionRoundDeleted", roundId, "Rodada removida.", clock.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return RoundCommandOutcome.Completed;
        }, cancellationToken);

    public Task<RoundCommandResult> ChangeStatusAsync(
        Guid competitionId,
        Guid roundId,
        RoundTransition transition,
        string version,
        CancellationToken cancellationToken) =>
        MutateAsync(competitionId, roundId, version, (competition, round, now) => transition switch
        {
            RoundTransition.OpenMarket => OpenMarketAsync(competition, round, now, cancellationToken),
            RoundTransition.ReopenForEditing => Task.FromResult(Reopen(round, now)),
            RoundTransition.SendToReview => BeginReviewAsync(
                competition, round, now, cancellationToken),
            RoundTransition.Cancel => Task.FromResult(CancelRound(round, now)),
            _ => Task.FromResult<RoundCommandResult?>(
                RoundCommandResult.Invalid(new RoundError("Transition", "Transição desconhecida."))),
        }, cancellationToken);

    public Task<RoundCommandResult> AddMatchAsync(
        Guid competitionId,
        Guid roundId,
        MatchInput match,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        return MutateAsync(competitionId, roundId, version, async (competition, round, now) =>
        {
            if (!round.AcceptsMatchChanges)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.StatusLocked);
            }

            var definition = Parse(competition, match, out var errors);
            if (definition is null)
            {
                return new RoundCommandResult(RoundCommandOutcome.Invalid, null, errors);
            }

            if (await EligibilityErrorAsync(competition.Id, definition, cancellationToken)
                .ConfigureAwait(false) is { } problem)
            {
                return RoundCommandResult.Invalid(problem);
            }

            dbContext.Matches.Add(Match.Create(
                Guid.CreateVersion7(), competition.Id, round.Id, definition, now));
            AddAudit("CompetitionMatchCreated", round.Id, "Partida criada na rodada.", now);
            return null;
        }, cancellationToken);
    }

    public Task<RoundCommandResult> UpdateMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        MatchInput match,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        return MutateAsync(competitionId, roundId, version, async (competition, round, now) =>
        {
            var existing = await dbContext.Matches
                .SingleOrDefaultAsync(
                    item => item.Id == matchId && item.RoundId == roundId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.NotFound);
            }

            var status = ParseStatus(match.Status);
            if (status is null)
            {
                return RoundCommandResult.Invalid(
                    new RoundError(nameof(MatchInput.Status), "Escolha marcada, adiada ou cancelada."));
            }

            var definition = Parse(competition, match, out var errors);
            if (definition is null)
            {
                return new RoundCommandResult(RoundCommandOutcome.Invalid, null, errors);
            }

            // Com o mercado aberto, a lista de jogos já vale para quem escalou: só o
            // horário e a situação continuam ajustáveis (01 §8).
            var changesPairing = existing.StageId != definition.StageId
                || existing.HomeTeamId != definition.HomeTeamId
                || existing.AwayTeamId != definition.AwayTeamId;
            if (changesPairing && !round.AcceptsMatchChanges)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.StatusLocked);
            }

            if (changesPairing
                && await EligibilityErrorAsync(competition.Id, definition, cancellationToken)
                    .ConfigureAwait(false) is { } problem)
            {
                return RoundCommandResult.Invalid(problem);
            }

            if (round.AcceptsMatchChanges)
            {
                existing.Update(definition, now);
            }

            try
            {
                Apply(existing, status.Value, definition.KickoffAt, now);
            }
            catch (InvalidOperationException exception)
            {
                return RoundCommandResult.Invalid(new RoundError(nameof(MatchInput.Status), exception.Message));
            }

            AddAudit("CompetitionMatchUpdated", round.Id, "Partida da rodada alterada.", now);
            return null;
        }, cancellationToken);
    }

    public Task<RoundCommandResult> RemoveMatchAsync(
        Guid competitionId,
        Guid roundId,
        Guid matchId,
        string version,
        CancellationToken cancellationToken) =>
        MutateAsync(competitionId, roundId, version, async (competition, round, now) =>
        {
            if (!round.AcceptsMatchChanges)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.StatusLocked);
            }

            var existing = await dbContext.Matches
                .SingleOrDefaultAsync(
                    item => item.Id == matchId && item.RoundId == roundId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return RoundCommandResult.Of(RoundCommandOutcome.NotFound);
            }

            dbContext.Matches.Remove(existing);
            AddAudit("CompetitionMatchDeleted", round.Id, "Partida removida da rodada.", now);
            return null;
        }, cancellationToken);

    private static void Apply(Match match, MatchStatus status, DateTimeOffset kickoffAt, DateTimeOffset now)
    {
        switch (status)
        {
            case MatchStatus.Postponed:
                match.Postpone(now);
                break;
            case MatchStatus.Cancelled:
                match.Cancel(now);
                break;
            default:
                match.Reschedule(kickoffAt, now);
                break;
        }
    }

    private async Task<RoundCommandResult?> OpenMarketAsync(
        Competition competition,
        Round round,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var kickoffs = await dbContext.Matches
            .Where(match => match.RoundId == round.Id && match.Status == MatchStatus.Scheduled)
            .Select(match => match.KickoffAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (kickoffs.Count == 0)
        {
            return RoundCommandResult.Invalid(
                new RoundError("Matches", "Marque ao menos uma partida antes de abrir o mercado."));
        }

        try
        {
            round.OpenMarket(kickoffs.Min(), competition.MarketCloseLeadTime, now);
        }
        catch (InvalidOperationException exception)
        {
            return RoundCommandResult.Invalid(new RoundError("MarketCloseAt", exception.Message));
        }

        AddAudit("CompetitionRoundMarketOpened", round.Id, "Mercado da rodada aberto.", now);
        return null;
    }

    private RoundCommandResult? Reopen(Round round, DateTimeOffset now)
    {
        try
        {
            round.ReopenForEditing(now);
        }
        catch (InvalidOperationException exception)
        {
            return RoundCommandResult.Invalid(new RoundError("Status", exception.Message));
        }

        AddAudit("CompetitionRoundReopened", round.Id, "Rodada voltou para rascunho.", now);
        return null;
    }

    private RoundCommandResult? CancelRound(Round round, DateTimeOffset now)
    {
        try
        {
            round.Cancel(now);
        }
        catch (InvalidOperationException exception)
        {
            return RoundCommandResult.Invalid(new RoundError("Status", exception.Message));
        }

        AddAudit("CompetitionRoundCancelled", round.Id, "Rodada cancelada.", now);
        return null;
    }

    private async Task<RoundCommandResult?> BeginReviewAsync(
        Competition competition,
        Round round,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var review = await BuildReviewAsync(competition, round, now, cancellationToken).ConfigureAwait(false);
        if (review.Pending.Count > 0)
        {
            return RoundCommandResult.Invalid(
                [.. review.Pending.Select(item => new RoundError("Review", item.Message))]);
        }

        try
        {
            round.BeginReview(now);
        }
        catch (InvalidOperationException exception)
        {
            return RoundCommandResult.Invalid(new RoundError("Status", exception.Message));
        }

        AddAudit("CompetitionRoundUnderReview", round.Id, "Rodada enviada para revisão.", now);
        return null;
    }

    /// <summary>
    /// Converte o horário local e monta a definição. O fuso é o do campeonato, não o de
    /// quem está com o navegador aberto.
    /// </summary>
    private static MatchDefinition? Parse(
        Competition competition,
        MatchInput match,
        out IReadOnlyList<RoundError> errors)
    {
        if (!CompetitionClock.TryToUtc(
            match.KickoffLocal, competition.TimeZoneId, out var kickoff, out var failure))
        {
            errors = [new(nameof(MatchInput.KickoffLocal), Explain(failure))];
            return null;
        }

        var definition = new MatchDefinition(
            match.StageId, match.HomeTeamId, match.AwayTeamId, kickoff);
        errors = definition.Validate();
        return errors.Count == 0 ? definition : null;
    }

    private static string Explain(LocalTimeFailure failure) => failure switch
    {
        LocalTimeFailure.DoesNotExist =>
            "Esse horário não existe no fuso do campeonato, por causa do horário de verão.",
        LocalTimeFailure.UnknownTimeZone =>
            "O fuso do campeonato não é reconhecido pelo servidor.",
        _ => "Informe a data e a hora do jogo.",
    };

    private static MatchStatus? ParseStatus(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? MatchStatus.Scheduled
            : Enum.TryParse<MatchStatus>(value, ignoreCase: true, out var status) && Enum.IsDefined(status)
                ? status
                : null;

    /// <summary>
    /// Confere que a fase é do campeonato e que os dois times foram confirmados nela. Em
    /// fase de grupos os dois precisam estar no mesmo grupo: é o que a fase significa.
    /// </summary>
    private async Task<RoundError?> EligibilityErrorAsync(
        Guid competitionId,
        MatchDefinition definition,
        CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == definition.StageId && item.CompetitionId == competitionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (stage is null)
        {
            return new(nameof(MatchDefinition.StageId), "Escolha uma fase deste campeonato.");
        }

        var participants = await dbContext.StageParticipants
            .AsNoTracking()
            .Where(participant => participant.StageId == definition.StageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var home = participants.SingleOrDefault(item => item.RealTeamId == definition.HomeTeamId);
        var away = participants.SingleOrDefault(item => item.RealTeamId == definition.AwayTeamId);
        if (home is null || away is null)
        {
            return new(
                home is null ? nameof(MatchDefinition.HomeTeamId) : nameof(MatchDefinition.AwayTeamId),
                "O time precisa estar confirmado nesta fase antes de jogar.");
        }

        return stage.Format == StageFormat.Groups && home.StageGroupId != away.StageGroupId
            ? new(nameof(MatchDefinition.AwayTeamId), "Numa fase de grupos, os dois times jogam no mesmo grupo.")
            : null;
    }

    /// <summary>
    /// Carrega a rodada, confere a versão lida, aplica <paramref name="change"/> e grava.
    /// Um resultado devolvido por <paramref name="change"/> aborta a gravação.
    /// </summary>
    private async Task<RoundCommandResult> MutateAsync(
        Guid competitionId,
        Guid roundId,
        string version,
        Func<Competition, Round, DateTimeOffset, Task<RoundCommandResult?>> change,
        CancellationToken cancellationToken)
    {
        var competition = await CompetitionAsync(competitionId, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return RoundCommandResult.Of(RoundCommandOutcome.NotFound);
        }

        var round = await RoundsOf(competitionId)
            .SingleOrDefaultAsync(item => item.Id == roundId, cancellationToken)
            .ConfigureAwait(false);
        if (round is null)
        {
            return RoundCommandResult.Of(RoundCommandOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, round.RowVersion, out var expectedVersion))
        {
            return RoundCommandResult.Of(RoundCommandOutcome.Conflict);
        }

        var now = clock.GetUtcNow();
        if (await change(competition, round, now).ConfigureAwait(false) is { } rejected)
        {
            dbContext.ChangeTracker.Clear();
            return rejected;
        }

        dbContext.Entry(round).Property(item => item.RowVersion).OriginalValue = expectedVersion;

        // Mexer só nas partidas não altera a linha da rodada; forçar o UPDATE mantém a
        // checagem de versão e faz a versão avançar para a próxima leitura.
        dbContext.Entry(round).Property(item => item.UpdatedAt).IsModified = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RoundCommandResult.Of(RoundCommandOutcome.Conflict);
        }

        return await CompletedAsync(competition, round, cancellationToken).ConfigureAwait(false);
    }

    private Task<Competition?> CompetitionAsync(Guid competitionId, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(competition => competition.Id == competitionId, cancellationToken);

    private IQueryable<Round> RoundsOf(Guid competitionId) =>
        dbContext.Rounds
            .Where(round => round.CompetitionId == competitionId)
            .OrderBy(round => round.Sequence);

    private async Task<RoundReviewView> BuildReviewAsync(
        Competition competition,
        Round round,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var matches = await (
            from match in dbContext.Matches.AsNoTracking()
            join home in dbContext.RealTeams.AsNoTracking() on match.HomeTeamId equals home.Id
            join away in dbContext.RealTeams.AsNoTracking() on match.AwayTeamId equals away.Id
            where match.CompetitionId == competition.Id && match.RoundId == round.Id
            orderby match.KickoffAt, home.Name
            select new ReviewMatchRow(
                match.Id,
                match.HomeTeamId,
                home.Name,
                match.AwayTeamId,
                away.Name,
                match.KickoffAt,
                match.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var matchIds = matches.Select(item => item.Id).ToArray();
        var sheets = await dbContext.MatchSheets
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id && matchIds.Contains(item.MatchId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sheetIds = sheets.Select(item => item.Id).ToArray();
        var appearances = await dbContext.AthleteAppearances
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id && sheetIds.Contains(item.MatchSheetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var events = await dbContext.StatEvents
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id && sheetIds.Contains(item.MatchSheetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sheetsByMatch = sheets.ToDictionary(item => item.MatchId);
        var appearancesBySheet = appearances.ToLookup(item => item.MatchSheetId);
        var eventsBySheet = events.ToLookup(item => item.MatchSheetId);
        var pending = new List<RoundReviewPendingView>();
        if (!matches.Any(item => item.Status == MatchStatus.Scheduled))
        {
            pending.Add(new(
                "no_scheduled_matches",
                "A rodada precisa ter ao menos uma partida marcada para entrar em revisão.",
                null));
        }

        var views = new List<RoundReviewMatchView>();
        foreach (var match in matches)
        {
            var requiresSheet = match.Status == MatchStatus.Scheduled;
            sheetsByMatch.TryGetValue(match.Id, out var sheet);
            if (requiresSheet && match.KickoffAt > now)
            {
                pending.Add(new(
                    "match_not_started",
                    $"{match.HomeTeamName} × {match.AwayTeamName} ainda não começou.",
                    match.Id));
            }

            if (requiresSheet && sheet is null)
            {
                pending.Add(new(
                    "sheet_missing",
                    $"Preencha a súmula de {match.HomeTeamName} × {match.AwayTeamName}.",
                    match.Id));
            }

            var sheetAppearances = sheet is null ? [] : appearancesBySheet[sheet.Id].ToArray();
            var sheetEvents = sheet is null ? [] : eventsBySheet[sheet.Id].ToArray();
            if (requiresSheet && sheet is not null)
            {
                foreach (var error in ValidateSheet(match, sheet, sheetAppearances, sheetEvents))
                {
                    pending.Add(new(
                        "sheet_invalid",
                        $"{match.HomeTeamName} × {match.AwayTeamName}: {error.Message}",
                        match.Id));
                }
            }

            views.Add(new(
                match.Id,
                match.HomeTeamName,
                match.AwayTeamName,
                CompetitionClock.ToLocalText(match.KickoffAt, competition.TimeZoneId),
                match.Status.ToString(),
                requiresSheet,
                sheet is not null,
                sheet?.HomeScore,
                sheet?.AwayScore,
                sheetAppearances.Count(item => item.DidPlay),
                [.. sheetEvents
                    .GroupBy(item => item.Type)
                    .OrderBy(group => group.Key)
                    .Select(group => new RoundReviewEventView(
                        group.Key.ToString(), group.Sum(item => item.Quantity))) ]));
        }

        var scheduled = matches.Count(item => item.Status == MatchStatus.Scheduled);
        return new(
            round.Id,
            round.Name,
            round.PhaseAt(now).ToString(),
            scheduled,
            matches.Count(item => item.Status == MatchStatus.Scheduled && sheetsByMatch.ContainsKey(item.Id)),
            pending.Count == 0,
            views,
            pending,
            Convert.ToBase64String(round.RowVersion));
    }

    private static IReadOnlyList<MatchSheetError> ValidateSheet(
        ReviewMatchRow match,
        MatchSheet sheet,
        IReadOnlyCollection<AthleteAppearance> appearances,
        IReadOnlyCollection<StatEvent> events)
    {
        var definitions = appearances.Select(appearance =>
        {
            var athleteEvents = events.Where(item => item.AthleteId == appearance.AthleteId).ToArray();
            var redCard = athleteEvents.FirstOrDefault(item => item.Type == StatEventType.RedCard);
            return new MatchSheetAppearanceDefinition(
                appearance.AthleteId,
                appearance.RealTeamId,
                appearance.Position,
                appearance.DidPlay,
                appearance.PlayedAsGoalkeeper,
                appearance.GoalsConceded,
                Quantity(athleteEvents, StatEventType.Goal),
                Quantity(athleteEvents, StatEventType.Assist),
                Quantity(athleteEvents, StatEventType.GoalkeeperSave),
                Quantity(athleteEvents, StatEventType.PenaltySave),
                Quantity(athleteEvents, StatEventType.YellowCard),
                Quantity(athleteEvents, StatEventType.RedCard),
                redCard?.RedCardReason,
                Quantity(athleteEvents, StatEventType.OwnGoal),
                Quantity(athleteEvents, StatEventType.PenaltyMiss));
        }).ToArray();
        return new MatchSheetDefinition(sheet.HomeScore, sheet.AwayScore, definitions)
            .Validate(match.HomeTeamId, match.AwayTeamId);
    }

    private static int Quantity(IEnumerable<StatEvent> events, StatEventType type) =>
        events.Where(item => item.Type == type).Sum(item => item.Quantity);

    private async Task<RoundCommandResult> CompletedAsync(
        Competition competition,
        Round round,
        CancellationToken cancellationToken)
    {
        var views = await ViewsAsync(competition, [round], cancellationToken).ConfigureAwait(false);
        return new RoundCommandResult(RoundCommandOutcome.Completed, views.Single(), []);
    }

    private async Task<IReadOnlyList<RoundView>> ViewsAsync(
        Competition competition,
        List<Round> rounds,
        CancellationToken cancellationToken)
    {
        if (rounds.Count == 0)
        {
            return [];
        }

        var roundIds = rounds.Select(round => round.Id).ToArray();
        var rows = await (
            from match in dbContext.Matches.AsNoTracking()
            join stage in dbContext.Stages.AsNoTracking() on match.StageId equals stage.Id
            join home in dbContext.RealTeams.AsNoTracking() on match.HomeTeamId equals home.Id
            join away in dbContext.RealTeams.AsNoTracking() on match.AwayTeamId equals away.Id
            join participant in dbContext.StageParticipants.AsNoTracking()
                on new { match.StageId, RealTeamId = match.HomeTeamId }
                equals new { participant.StageId, participant.RealTeamId } into homeParticipants
            from participant in homeParticipants.DefaultIfEmpty()
            join groupItem in dbContext.Set<StageGroup>().AsNoTracking()
                on participant.StageGroupId equals groupItem.Id into groups
            from groupItem in groups.DefaultIfEmpty()
            where roundIds.Contains(match.RoundId)
            select new
            {
                match.Id,
                match.RoundId,
                match.StageId,
                StageName = stage.Name,
                match.HomeTeamId,
                HomeTeamName = home.Name,
                match.AwayTeamId,
                AwayTeamName = away.Name,
                GroupName = groupItem == null ? null : groupItem.Name,
                match.KickoffAt,
                match.Status,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var zone = competition.TimeZoneId;
        return
        [
            .. rounds.OrderBy(round => round.Sequence).Select(round => new RoundView(
                round.Id,
                round.Name,
                round.Sequence,
                round.Status.ToString(),
                round.PhaseAt(now).ToString(),
                round.MarketCloseAt,
                round.MarketCloseAt is { } closeAt ? CompetitionClock.ToLocalText(closeAt, zone) : null,
                [.. rows
                    .Where(row => row.RoundId == round.Id)
                    .OrderBy(row => row.KickoffAt)
                    .ThenBy(row => row.HomeTeamName)
                    .Select(row => new MatchView(
                        row.Id,
                        row.StageId,
                        row.StageName,
                        row.HomeTeamId,
                        row.HomeTeamName,
                        row.AwayTeamId,
                        row.AwayTeamName,
                        row.GroupName,
                        row.KickoffAt,
                        CompetitionClock.ToLocalText(row.KickoffAt, zone),
                        row.Status.ToString()))],
                Convert.ToBase64String(round.RowVersion)))
        ];
    }

    private void AddAudit(string action, Guid targetId, string reason, DateTimeOffset occurredAt) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            action,
            targetId,
            reason,
            occurredAt));

    private sealed record ReviewMatchRow(
        Guid Id,
        Guid HomeTeamId,
        string HomeTeamName,
        Guid AwayTeamId,
        string AwayTeamName,
        DateTimeOffset KickoffAt,
        MatchStatus Status);

    /// <summary>
    /// Serializa as operações que mexem na ordem das rodadas de um mesmo campeonato,
    /// como nas fases: read committed com UPDLOCK na linha do campeonato.
    /// </summary>
    private Task<T> InCompetitionLockAsync<T>(
        Guid competitionId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            await dbContext.Competitions
                .FromSql($"""
                    SELECT * FROM [competitions].[Competitions] WITH (UPDLOCK, ROWLOCK)
                    WHERE [Id] = {competitionId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await operation().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }
}
