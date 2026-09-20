using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.Scoring;
using Fut7Fantasy.Infrastructure.Competitions;
using Fut7Fantasy.Infrastructure.Fantasy;
using Fut7Fantasy.Infrastructure.Importing;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Scoring;

public sealed class RoundPublicationService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock,
    ICompetitionRoundService rounds,
    LineupSnapshotMaterializer snapshots) : IRoundPublicationService
{
    private readonly MatchSheetStore _sheets = new(dbContext);

    /// <summary>
    /// Serializa pela linha do campeonato, como o catálogo e as importações: a publicação
    /// lê preços e escreve os novos, e nenhuma outra escrita pode passar no meio.
    /// </summary>
    public Task<RoundPublicationResult> PublishAsync(
        Guid competitionId,
        Guid roundId,
        string version,
        CancellationToken cancellationToken) =>
        CompetitionLock.RunAsync(dbContext, competitionId, async () =>
        {
            // Retratos das rodadas fechadas antes de tudo: a apuração lê os desta rodada, e
            // a materialização pode limpar o rastreamento se houver corrida.
            await snapshots.EnsureClosedRoundsAsync(competitionId, cancellationToken).ConfigureAwait(false);

            var competition = await dbContext.Competitions
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
                .ConfigureAwait(false);
            var round = await dbContext.Rounds
                .SingleOrDefaultAsync(
                    item => item.CompetitionId == competitionId && item.Id == roundId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (competition is null || round is null)
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.NotFound);
            }

            // O mesmo pedido repetido — duplo clique, nova tentativa — não apura de novo.
            if (round.Status == RoundStatus.Published)
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.Completed);
            }

            if (!RowVersions.Matches(version, round.RowVersion, out var expectedVersion))
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.Conflict);
            }

            if (round.Status != RoundStatus.UnderReview)
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.StatusLocked);
            }

            var pending = await PendingAsync(competition, round, cancellationToken).ConfigureAwait(false);
            if (pending.Count > 0)
            {
                return RoundPublicationResult.Invalid([.. pending]);
            }

            var now = clock.GetUtcNow();
            var closedAt = round.MarketCloseAt!.Value;
            var rules = await RulesAsync(competition, cancellationToken).ConfigureAwait(false);
            var revision = 1 + (await dbContext.RoundCalculations
                .Where(item => item.RoundId == round.Id)
                .MaxAsync(item => (int?)item.Revision, cancellationToken)
                .ConfigureAwait(false) ?? 0);

            var calculation = RoundCalculation.Compute(
                Guid.CreateVersion7(),
                competition.Id,
                round.Id,
                revision,
                rules,
                await PerformancesAsync(competition, round, cancellationToken).ConfigureAwait(false),
                await CatalogAsync(competition, closedAt, cancellationToken).ConfigureAwait(false),
                await LineupsAsync(round, cancellationToken).ConfigureAwait(false),
                now,
                UserId);
            dbContext.RoundCalculations.Add(calculation);

            round.Publish(
                now,
                CorrectionWindow.ConsolidatesAt(
                    closedAt, now, competition.CorrectionWindowBusinessDays, competition.TimeZoneId));
            dbContext.Entry(round).Property(item => item.RowVersion).OriginalValue = expectedVersion;
            dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
                UserId,
                "CompetitionRoundPublished",
                round.Id,
                $"Rodada publicada com a revisão {revision} da apuração.",
                now));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Inclui a concorrência da rodada e a revisão repetida: nada foi gravado.
                dbContext.ChangeTracker.Clear();
                return RoundPublicationResult.Of(RoundPublicationOutcome.Conflict);
            }

            return RoundPublicationResult.Of(RoundPublicationOutcome.Completed);
        }, cancellationToken);

    /// <summary>
    /// A conferência refeita na hora (súmulas completas e coerentes) e a ordem das rodadas:
    /// a valorização de uma rodada parte do preço deixado pela anterior, então ninguém
    /// publica a segunda com a primeira ainda aberta.
    /// </summary>
    private async Task<List<RoundError>> PendingAsync(
        Competition competition,
        Round round,
        CancellationToken cancellationToken)
    {
        var review = await rounds.ReviewAsync(competition.Id, round.Id, cancellationToken).ConfigureAwait(false);
        List<RoundError> errors = [.. review?.Pending.Select(item => new RoundError("Review", item.Message)) ?? []];

        var earlier = await dbContext.Rounds
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id
                && item.Id != round.Id
                && item.MarketCloseAt != null
                && item.MarketCloseAt < round.MarketCloseAt
                && item.Status != RoundStatus.Published
                && item.Status != RoundStatus.Cancelled)
            .OrderBy(item => item.MarketCloseAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (earlier is not null)
        {
            errors.Add(new(
                "Round",
                $"Publique antes a {earlier.Name}: os preços andam na ordem das rodadas."));
        }

        return errors;
    }

    /// <summary>
    /// O campeonato fica na versão de regra da primeira rodada publicada: recalibrar a
    /// modalidade no meio não muda o jogo de quem já está jogando (decisão de 2026-09-19).
    /// </summary>
    private async Task<ScoringRuleSet> RulesAsync(Competition competition, CancellationToken cancellationToken)
    {
        var version = await dbContext.RoundCalculations
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id)
            .OrderBy(item => item.CalculatedAt)
            .Select(item => (int?)item.ScoringRuleSetVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return version is { } used
            ? ScoringRuleSets.Find(competition.Modality, used)
                ?? throw new InvalidOperationException($"A regra de pontuação v{used} não existe no código.")
            : ScoringRuleSets.CurrentFor(competition.Modality);
    }

    /// <summary>
    /// Cada atleta de cada partida marcada da rodada, com os gols que o time dele sofreu.
    /// Partida adiada ou cancelada não entra: os atletas dela contam como quem não jogou.
    /// </summary>
    private async Task<List<MatchPerformance>> PerformancesAsync(
        Competition competition,
        Round round,
        CancellationToken cancellationToken)
    {
        var matches = await dbContext.Matches
            .AsNoTracking()
            .Where(match => match.CompetitionId == competition.Id
                && match.RoundId == round.Id
                && match.Status == MatchStatus.Scheduled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var matchIds = matches.Select(match => match.Id).ToArray();
        var sheets = await dbContext.MatchSheets
            .AsNoTracking()
            .Where(sheet => sheet.CompetitionId == competition.Id && matchIds.Contains(sheet.MatchId))
            .ToDictionaryAsync(sheet => sheet.MatchId, cancellationToken)
            .ConfigureAwait(false);

        List<MatchPerformance> performances = [];
        foreach (var match in matches)
        {
            var sheet = sheets[match.Id];
            foreach (var appearance in await _sheets.DefinitionsAsync(sheet, cancellationToken).ConfigureAwait(false))
            {
                var conceded = appearance.RealTeamId == match.HomeTeamId ? sheet.AwayScore : sheet.HomeScore;
                performances.Add(new(match.Id, conceded, appearance));
            }
        }

        return performances;
    }

    /// <summary>Todo ativo do campeonato, com o preço que valia antes desta rodada.</summary>
    private async Task<List<PricedAsset>> CatalogAsync(
        Competition competition,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken)
    {
        var profile = competition.ModalityProfile;
        var prices = await AssetPrices.CurrentAsync(dbContext, competition.Id, cancellationToken, before: closedAt)
            .ConfigureAwait(false);
        var athletes = await (
                from athlete in dbContext.Athletes.AsNoTracking()
                join registration in dbContext.RosterRegistrations.AsNoTracking()
                    on athlete.Id equals registration.AthleteId
                where athlete.CompetitionId == competition.Id
                select new
                {
                    athlete.Id,
                    athlete.Position,
                    registration.RealTeamId,
                    registration.PriceTier,
                    registration.InitialPriceOverride,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var coaches = await dbContext.Coaches
            .AsNoTracking()
            .Where(coach => coach.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. athletes.Select(athlete => new PricedAsset(
                AssetKind.Athlete,
                athlete.Id,
                athlete.Position,
                athlete.RealTeamId,
                prices.TryGetValue((AssetKind.Athlete, athlete.Id), out var price)
                    ? price
                    : profile.InitialAthletePrice(athlete.Position, athlete.PriceTier, athlete.InitialPriceOverride))),
            .. coaches.Select(coach => new PricedAsset(
                AssetKind.Coach,
                coach.Id,
                null,
                coach.RealTeamId,
                prices.TryGetValue((AssetKind.Coach, coach.Id), out var price)
                    ? price
                    : profile.InitialCoachPrice(coach.PriceTier, coach.InitialPriceOverride))),
        ];
    }

    private async Task<List<EntryLineup>> LineupsAsync(Round round, CancellationToken cancellationToken)
    {
        var frozen = await dbContext.LineupSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Slots)
            .Where(snapshot => snapshot.RoundId == round.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. frozen.Select(snapshot => new EntryLineup(snapshot.EntryId, FrozenLineup.From(snapshot)))];
    }

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
