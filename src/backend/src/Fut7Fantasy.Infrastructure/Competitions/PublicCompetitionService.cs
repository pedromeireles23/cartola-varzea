using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

public sealed class PublicCompetitionService(Fut7FantasyDbContext dbContext) : IPublicCompetitionService
{
    /// <summary>Teto de linhas da busca enquanto não existe paginação (Fase 8).</summary>
    public const int MaxResults = 50;

    /// <summary>Acima disso o texto não filtra melhor, só custa mais no banco.</summary>
    public const int MaxQueryLength = 80;

    private const char LikeEscape = '\\';

    public async Task<IReadOnlyList<PublicCompetitionSummary>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var rows = WithOrganization(Published());
        if (Term(query) is { } term)
        {
            var pattern = $"%{Escape(term)}%";
            var escape = LikeEscape.ToString();
            rows = rows.Where(row =>
                EF.Functions.Like(row.Competition.Name, pattern, escape)
                || EF.Functions.Like(row.Competition.Season, pattern, escape)
                || EF.Functions.Like(row.OrganizationName, pattern, escape));
        }

        var found = await rows
            .OrderByDescending(row => row.Competition.PublishedAt)
            .ThenBy(row => row.Competition.Name)
            .Take(MaxResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. found.Select(row => new PublicCompetitionSummary(
                row.Competition.Slug!,
                row.Competition.Name,
                row.Competition.Season,
                row.Competition.Modality.ToString(),
                row.OrganizationName,
                row.Competition.PublishedAt!.Value))
        ];
    }

    public async Task<PublicCompetitionView?> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var found = await WithOrganization(Published().Where(competition => competition.Slug == slug))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var competition = found.Competition;
        return new PublicCompetitionView(
            competition.Slug!,
            competition.Name,
            competition.Season,
            competition.Modality.ToString(),
            found.OrganizationName,
            competition.TimeZoneId,
            competition.PublishedAt!.Value,
            ModalityProfileView.From(competition.ModalityProfile),
            await StagesAsync(competition.Id, cancellationToken).ConfigureAwait(false),
            await TeamsAsync(competition.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Base de toda leitura pública: campeonato publicado e com endereço. O filtro fica
    /// aqui, e não em cada consulta, para que um rascunho nunca escape por esquecimento.
    /// </summary>
    private IQueryable<Competition> Published() =>
        dbContext.Competitions
            .AsNoTracking()
            .Where(competition =>
                competition.Status == CompetitionStatus.Published && competition.Slug != null);

    /// <summary>
    /// Acrescenta o nome da organização, que a busca também filtra. A projeção usa
    /// inicializador de objeto, e não construtor: só assim o EF consegue continuar
    /// traduzindo `row.Competition.Name` num filtro aplicado depois.
    /// </summary>
    private IQueryable<PublishedRow> WithOrganization(IQueryable<Competition> competitions) =>
        from competition in competitions
        join organization in dbContext.Organizations.AsNoTracking()
            on competition.OrganizationId equals organization.Id
        select new PublishedRow { Competition = competition, OrganizationName = organization.Name };

    private async Task<IReadOnlyList<PublicStageView>> StagesAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(stage => stage.CompetitionId == competitionId)
            .OrderBy(stage => stage.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (stages.Count == 0)
        {
            return [];
        }

        var stageIds = stages.Select(stage => stage.Id).ToArray();
        var participants = await (
            from participant in dbContext.StageParticipants.AsNoTracking()
            join team in dbContext.RealTeams.AsNoTracking()
                on participant.RealTeamId equals team.Id
            join groupItem in dbContext.Set<StageGroup>().AsNoTracking()
                on participant.StageGroupId equals groupItem.Id into participantGroups
            from groupItem in participantGroups.DefaultIfEmpty()
            where stageIds.Contains(participant.StageId)
            select new
            {
                participant.StageId,
                TeamName = team.Name,
                GroupName = groupItem == null ? null : groupItem.Name,
                GroupSequence = groupItem == null ? 0 : groupItem.Sequence,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. stages.Select(stage =>
            {
                var ofStage = participants.Where(row => row.StageId == stage.Id).ToList();
                return new PublicStageView(
                    stage.Name,
                    stage.Format.ToString(),
                    stage.Sequence,
                    [.. ofStage
                        .Where(row => row.GroupName is not null)
                        .GroupBy(row => new { row.GroupName, row.GroupSequence })
                        .OrderBy(group => group.Key.GroupSequence)
                        .Select(group => new PublicStageGroupView(
                            group.Key.GroupName!,
                            [.. group.Select(row => row.TeamName).Order(StringComparer.CurrentCulture)]))],
                    [.. ofStage
                        .Where(row => row.GroupName is null)
                        .Select(row => row.TeamName)
                        .Order(StringComparer.CurrentCulture)]);
            })
        ];
    }

    /// <summary>Times ativos com quantos atletas seguem inscritos neles.</summary>
    private async Task<IReadOnlyList<PublicTeamView>> TeamsAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var teams = await (
            from team in dbContext.RealTeams.AsNoTracking()
            where team.CompetitionId == competitionId && team.ArchivedAt == null
            orderby team.Name
            select new PublicTeamView(
                team.Name,
                dbContext.RosterRegistrations.Count(registration =>
                    registration.RealTeamId == team.Id
                    && registration.Status == RosterRegistrationStatus.Active)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return teams;
    }

    private static string? Term(string? query)
    {
        var trimmed = query?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed[..Math.Min(trimmed.Length, MaxQueryLength)];
    }

    /// <summary>
    /// Neutraliza os curingas do LIKE: sem isso, buscar por `%` devolveria o catálogo
    /// inteiro e `[a-z]` viraria uma classe de caracteres.
    /// </summary>
    private static string Escape(string term) => term
        .Replace($"{LikeEscape}", $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);

    private sealed class PublishedRow
    {
        public required Competition Competition { get; init; }

        public required string OrganizationName { get; init; }
    }
}
