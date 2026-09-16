using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class CompetitionManagementService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : ICompetitionManagementService
{
    public async Task<IReadOnlyList<CompetitionSummaryView>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // O filtro por organização é explícito mesmo com a policy na rota (03 §8).
        var competitions = await dbContext.Competitions
            .AsNoTracking()
            .Where(competition => competition.OrganizationId == organizationId)
            .OrderByDescending(competition => competition.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. competitions.Select(competition => new CompetitionSummaryView(
            competition.Id,
            competition.Name,
            competition.Season,
            competition.Modality.ToString(),
            competition.Status.ToString(),
            competition.UpdatedAt))];
    }

    public async Task<CompetitionCommandResult> CreateDraftAsync(
        Guid organizationId,
        CompetitionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { Count: > 0 } errors)
        {
            return new CompetitionCommandResult(CompetitionCommandOutcome.Invalid, null, errors);
        }

        var userId = RequiredUserId();
        var now = clock.GetUtcNow();
        var competition = Competition.CreateDraft(Guid.CreateVersion7(), organizationId, userId, settings, now);
        dbContext.Competitions.Add(competition);
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            userId, "CompetitionDraftCreated", competition.Id, "Campeonato criado em rascunho.", now));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await CompletedAsync(competition.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompetitionDetailsView?> GetAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var userId = RequiredUserId();
        var found = await (
                from competition in dbContext.Competitions.AsNoTracking()
                join organization in dbContext.Organizations.AsNoTracking()
                    on competition.OrganizationId equals organization.Id
                join member in dbContext.OrganizationMembers.AsNoTracking()
                    on organization.Id equals member.OrganizationId
                where competition.Id == competitionId && member.UserId == userId
                select new { Competition = competition, OrganizationName = organization.Name, member.Role })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var item = found.Competition;
        return new CompetitionDetailsView(
            item.Id,
            item.OrganizationId,
            found.OrganizationName,
            found.Role.ToString(),
            item.Name,
            item.Season,
            item.Modality.ToString(),
            item.Status.ToString(),
            item.TimeZoneId,
            (int)item.MarketCloseLeadTime.TotalMinutes,
            item.ResultsSlaBusinessDays,
            item.CorrectionWindowBusinessDays,
            item.CanChangeModality,
            ModalityProfileView.From(item.ModalityProfile),
            item.CreatedAt,
            item.UpdatedAt,
            Convert.ToBase64String(item.RowVersion));
    }

    public async Task<CompetitionCommandResult> UpdateSettingsAsync(
        Guid competitionId,
        CompetitionSettings settings,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { Count: > 0 } errors)
        {
            return new CompetitionCommandResult(CompetitionCommandOutcome.Invalid, null, errors);
        }

        var competition = await dbContext.Competitions
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
            .ConfigureAwait(false);
        if (competition is null)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.NotFound);
        }

        // Uma versão ilegível nunca é a atual: vale como edição feita sobre dados velhos.
        if (!TryReadVersion(version, out var expectedVersion)
            || !expectedVersion.AsSpan().SequenceEqual(competition.RowVersion))
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.Conflict);
        }

        if (settings.Modality != competition.Modality && !competition.CanChangeModality)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.ModalityLocked);
        }

        var now = clock.GetUtcNow();
        competition.UpdateSettings(settings, now);

        // A comparação acima cobre quem leu antes; a versão original no UPDATE cobre quem
        // salvou entre a leitura e esta gravação.
        dbContext.Entry(competition).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            RequiredUserId(),
            "CompetitionSettingsUpdated",
            competition.Id,
            "Configuração do campeonato alterada.",
            now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CompetitionCommandResult.Of(CompetitionCommandOutcome.Conflict);
        }

        return await CompletedAsync(competition.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CompetitionCommandResult> CompletedAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(competitionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O campeonato gravado não pôde ser lido de volta.");
        return new CompetitionCommandResult(CompetitionCommandOutcome.Completed, view, []);
    }

    private static bool TryReadVersion(string version, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var buffer = new byte[version.Length];
        if (!Convert.TryFromBase64String(version, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    private Guid RequiredUserId() => currentUser.Id
        ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada.");
}
