using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Fantasy;
using Fut7Fantasy.Domain.Notifications;
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
            var catalog = await CatalogAsync(competition, closedAt, cancellationToken).ConfigureAwait(false);

            // O motivo declarado na reabertura vai para a revisão que esta publicação
            // grava; `Round.Publish` encerra a reabertura logo abaixo.
            var applied = await ApplyAsync(
                    competition, round, rules, catalog, now, round.CorrectionReason, cancellationToken)
                .ConfigureAwait(false);
            var chained = await ChainAsync(
                    competition, round, rules, catalog, applied.Calculation, now, cancellationToken)
                .ConfigureAwait(false);

            round.Publish(
                now,
                CorrectionWindow.ConsolidatesAt(
                    closedAt, now, competition.CorrectionWindowBusinessDays, competition.TimeZoneId));
            dbContext.Entry(round).Property(item => item.RowVersion).OriginalValue = expectedVersion;
            dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
                UserId,
                "CompetitionRoundPublished",
                round.Id,
                Explain(applied, chained),
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

            return new(
                RoundPublicationOutcome.Completed,
                [],
                new(applied.Calculation.Revision, applied.ChangedEntries, chained.Count));
        }, cancellationToken);

    /// <summary>
    /// Reabre sob a mesma trava da publicação. Nada é recalculado aqui: a apuração vigente
    /// continua sendo a última revisão gravada, e quem joga segue lendo os números dela com
    /// o aviso de correção em andamento. A súmula volta a aceitar edição porque a rodada
    /// volta para conferência.
    /// </summary>
    public Task<RoundPublicationResult> ReopenAsync(
        Guid competitionId,
        Guid roundId,
        string version,
        string? reason,
        CancellationToken cancellationToken) =>
        CompetitionLock.RunAsync(dbContext, competitionId, async () =>
        {
            var round = await dbContext.Rounds
                .SingleOrDefaultAsync(
                    item => item.CompetitionId == competitionId && item.Id == roundId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (round is null)
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.NotFound);
            }

            if (!RowVersions.Matches(version, round.RowVersion, out var expectedVersion))
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.Conflict);
            }

            if (round.Status != RoundStatus.Published)
            {
                return RoundPublicationResult.Of(RoundPublicationOutcome.StatusLocked);
            }

            var now = clock.GetUtcNow();
            try
            {
                round.ReopenForCorrection(now, reason);
            }
            catch (InvalidOperationException exception)
            {
                dbContext.ChangeTracker.Clear();
                return RoundPublicationResult.Invalid(new RoundError("Reason", exception.Message));
            }

            dbContext.Entry(round).Property(item => item.RowVersion).OriginalValue = expectedVersion;
            dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
                UserId,
                "CompetitionRoundReopenedForCorrection",
                round.Id,
                round.CorrectionReason is { } declared
                    ? $"Rodada reaberta para correção: {declared}"
                    : "Rodada reaberta para correção enquanto o resultado ainda era provisório.",
                now));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear();
                return RoundPublicationResult.Of(RoundPublicationOutcome.Conflict);
            }

            return RoundPublicationResult.Of(RoundPublicationOutcome.Completed);
        }, cancellationToken);

    /// <summary>Uma revisão recém-calculada e quantas participações ela mexeu.</summary>
    private sealed record Applied(RoundCalculation Calculation, int ChangedEntries);

    private static string Explain(Applied applied, IReadOnlyCollection<Applied> chained)
    {
        if (applied.Calculation.Revision == 1)
        {
            return "Rodada publicada com a revisão 1 da apuração.";
        }

        var text = $"Rodada republicada com a revisão {applied.Calculation.Revision} da apuração; "
            + $"{applied.ChangedEntries} participações mudaram de pontuação";
        return chained.Count == 0
            ? $"{text}."
            : $"{text}; {chained.Count} rodadas seguintes recalculadas, "
                + $"mexendo em {chained.Sum(item => item.ChangedEntries)} participações.";
    }

    /// <summary>
    /// Apura a rodada numa revisão nova e conta quantas participações mudaram de total em
    /// relação à revisão anterior — o que a auditoria registra e a tela resume.
    /// </summary>
    private async Task<Applied> ApplyAsync(
        Competition competition,
        Round round,
        ScoringRuleSet rules,
        IReadOnlyCollection<PricedAsset> catalog,
        DateTimeOffset now,
        string? reason,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.RoundCalculations
            .AsNoTracking()
            .Where(item => item.RoundId == round.Id)
            .OrderByDescending(item => item.Revision)
            .Select(item => new { item.Id, item.Revision })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var calculation = RoundCalculation.Compute(
            Guid.CreateVersion7(),
            competition.Id,
            round.Id,
            (current?.Revision ?? 0) + 1,
            rules,
            await PerformancesAsync(competition, round, cancellationToken).ConfigureAwait(false),
            catalog,
            await LineupsAsync(round, cancellationToken).ConfigureAwait(false),
            now,
            UserId,
            reason);
        dbContext.RoundCalculations.Add(calculation);
        await NotifyAsync(calculation, now, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new(calculation, 0);
        }

        var previous = await dbContext.EntryRoundResults
            .AsNoTracking()
            .Where(item => item.CalculationId == current.Id)
            .ToDictionaryAsync(item => item.EntryId, item => item.Total, cancellationToken)
            .ConfigureAwait(false);
        return new(
            calculation,
            calculation.Entries.Count(entry =>
                !previous.TryGetValue(entry.EntryId, out var total) || total != entry.Total));
    }

    /// <summary>
    /// A correção de uma rodada move o preço de todo ativo, e cada rodada seguinte partiu do
    /// preço antigo. Elas ganham revisão nova na ordem em que o mercado fechou, cada uma
    /// partindo do preço deixado pela anterior, dentro desta mesma transação: ninguém chega
    /// a ler o campeonato com metade das rodadas recalculada (ADR-003).
    /// </summary>
    private async Task<List<Applied>> ChainAsync(
        Competition competition,
        Round round,
        ScoringRuleSet rules,
        IReadOnlyCollection<PricedAsset> catalog,
        RoundCalculation previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var following = await dbContext.Rounds
            .AsNoTracking()
            .Where(item => item.CompetitionId == competition.Id
                && item.Id != round.Id
                && item.Status == RoundStatus.Published
                && item.MarketCloseAt != null
                && item.MarketCloseAt > round.MarketCloseAt)
            .OrderBy(item => item.MarketCloseAt)
            .ThenBy(item => item.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Applied> chained = [];
        foreach (var next in following)
        {
            var applied = await ApplyAsync(
                competition,
                next,
                rules,
                Reprice(catalog, previous),
                now,
                $"Recalculada porque a {round.Name} foi corrigida.",
                cancellationToken).ConfigureAwait(false);
            chained.Add(applied);
            previous = applied.Calculation;
        }

        return chained;
    }

    /// <summary>
    /// Avisa cada participação apurada, na mesma transação: a primeira revisão anuncia o
    /// resultado, as seguintes anunciam a correção. A chave única por conta, rodada e
    /// revisão faz a repetição não duplicar o aviso, como a própria apuração.
    /// </summary>
    private async Task NotifyAsync(
        RoundCalculation calculation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entryIds = calculation.Entries.Select(entry => entry.EntryId).ToArray();
        if (entryIds.Length == 0)
        {
            return;
        }

        var users = await dbContext.FantasyEntries
            .AsNoTracking()
            .Where(entry => entryIds.Contains(entry.Id))
            .Select(entry => entry.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.Notifications.AddRange(users.Select(user => Notification.ForRound(
            user, calculation.CompetitionId, calculation.RoundId, calculation.Revision, now)));
    }

    /// <summary>O mesmo catálogo com os preços que a apuração anterior deixou.</summary>
    private static List<PricedAsset> Reprice(
        IReadOnlyCollection<PricedAsset> catalog,
        RoundCalculation previous)
    {
        var prices = previous.Prices.ToDictionary(
            change => (change.Kind, change.AssetId), change => change.NewPrice);
        return
        [
            .. catalog.Select(asset => prices.TryGetValue((asset.Kind, asset.AssetId), out var price)
                ? asset with { Price = price }
                : asset),
        ];
    }

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
    private async Task<IReadOnlyCollection<PricedAsset>> CatalogAsync(
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
