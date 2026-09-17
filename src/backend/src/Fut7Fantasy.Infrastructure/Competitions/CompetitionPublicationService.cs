using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class CompetitionPublicationService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICompetitionPublicationService
{
    public async Task<CompetitionReadinessView?> GetReadinessAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var report = await EvaluateAsync(competition, cancellationToken).ConfigureAwait(false);
        return CompetitionReadinessView.From(competition, report);
    }

    public async Task<CompetitionPublicationResult> SetPublishedAsync(
        Guid competitionId,
        bool published,
        string version,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return CompetitionPublicationResult.Of(CompetitionPublicationOutcome.NotFound);
        }

        if (!RowVersions.Matches(version, competition.RowVersion, out var expectedVersion))
        {
            return CompetitionPublicationResult.Of(CompetitionPublicationOutcome.Conflict);
        }

        var report = await EvaluateAsync(competition, cancellationToken).ConfigureAwait(false);
        if (published && !competition.IsPublished && !report.CanPublish)
        {
            return new CompetitionPublicationResult(
                CompetitionPublicationOutcome.NotReady,
                CompetitionReadinessView.From(competition, report));
        }

        // Pedir o estado que já vale é uma confirmação, não uma mudança: nada é gravado.
        if (published == competition.IsPublished)
        {
            return new CompetitionPublicationResult(
                CompetitionPublicationOutcome.Completed,
                CompetitionReadinessView.From(competition, report));
        }

        var now = clock.GetUtcNow();
        if (published)
        {
            competition.Publish(now);
        }
        else
        {
            competition.Unpublish(now);
        }

        dbContext.Entry(competition).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            published ? "CompetitionPublished" : "CompetitionUnpublished",
            competition.Id,
            published ? "Campeonato publicado." : "Campeonato voltou para rascunho.",
            now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CompetitionPublicationResult.Of(CompetitionPublicationOutcome.Conflict);
        }

        return new CompetitionPublicationResult(
            CompetitionPublicationOutcome.Completed,
            CompetitionReadinessView.From(competition, report));
    }

    /// <summary>
    /// Monta o retrato do catálogo e deixa o domínio decidir. As contagens saem de
    /// consultas separadas de propósito: juntar fases, times e atletas numa só produziria
    /// um produto cartesiano difícil de ler e de conferir.
    /// </summary>
    private async Task<CompetitionReadinessReport> EvaluateAsync(
        Competition competition,
        CancellationToken cancellationToken)
    {
        var competitionId = competition.Id;
        var stages = await (
            from stage in dbContext.Stages.AsNoTracking()
            where stage.CompetitionId == competitionId
            orderby stage.Sequence
            select new StageReadiness(
                stage.Name,
                dbContext.StageParticipants.Count(participant => participant.StageId == stage.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var teams = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.ArchivedAt == null)
            .OrderBy(team => team.Name)
            .Select(team => new { team.Id, team.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Atleta disponível é o que segue inscrito num time que não foi arquivado.
        var athletes = await (
            from registration in dbContext.RosterRegistrations.AsNoTracking()
            join athlete in dbContext.Athletes.AsNoTracking()
                on registration.AthleteId equals athlete.Id
            join team in dbContext.RealTeams.AsNoTracking()
                on registration.RealTeamId equals team.Id
            where registration.CompetitionId == competitionId
                && registration.Status == RosterRegistrationStatus.Active
                && team.ArchivedAt == null
            select new
            {
                RealTeamId = team.Id,
                athlete.Position,
                registration.PriceTier,
                registration.InitialPriceOverride,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var coaches = await (
            from coach in dbContext.Coaches.AsNoTracking()
            join team in dbContext.RealTeams.AsNoTracking()
                on coach.RealTeamId equals team.Id
            where coach.CompetitionId == competitionId && team.ArchivedAt == null
            select new { coach.PriceTier, coach.InitialPriceOverride })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tiers = athletes
            .Select(athlete => athlete.PriceTier)
            .Concat(coaches.Select(coach => coach.PriceTier))
            .Distinct()
            .ToList();
        var hasExactPrice = athletes.Any(athlete => athlete.InitialPriceOverride is not null)
            || coaches.Any(coach => coach.InitialPriceOverride is not null);

        var snapshot = new CompetitionReadinessSnapshot(
            stages,
            [.. teams.Select(team => new RealTeamReadiness(
                team.Name,
                athletes.Count(athlete => athlete.RealTeamId == team.Id)))],
            athletes
                .GroupBy(athlete => athlete.Position)
                .ToDictionary(group => group.Key, group => group.Count()),
            tiers.Count == 1 && !hasExactPrice);

        return CompetitionReadiness.Evaluate(competition.ModalityProfile, snapshot);
    }
}
