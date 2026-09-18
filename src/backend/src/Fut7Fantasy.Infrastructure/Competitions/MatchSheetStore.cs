using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

/// <summary>Atleta que pode constar da súmula: inscrito num dos dois times no horário do jogo.</summary>
internal sealed record SheetAthlete(
    Guid AthleteId,
    Guid RealTeamId,
    string SportingName,
    Position Position);

/// <summary>
/// Leitura e gravação da súmula compartilhadas pelo editor e pela importação por rodada.
/// Os dois caminhos gravam a mesma coisa; se cada um tivesse a sua cópia, a súmula
/// importada acabaria diferente da lançada na tela.
/// </summary>
internal sealed class MatchSheetStore(Fut7FantasyDbContext dbContext)
{
    /// <summary>Elenco elegível da partida, na ordem em que a tela e o modelo mostram.</summary>
    public Task<List<SheetAthlete>> RosterAsync(Match match, CancellationToken cancellationToken) =>
        (
            from registration in dbContext.RosterRegistrations.AsNoTracking()
            join athlete in dbContext.Athletes.AsNoTracking() on registration.AthleteId equals athlete.Id
            where registration.CompetitionId == match.CompetitionId
                && athlete.CompetitionId == match.CompetitionId
                && registration.RegisteredAt <= match.KickoffAt
                && (registration.ReleasedAt == null || registration.ReleasedAt >= match.KickoffAt)
                && (registration.RealTeamId == match.HomeTeamId || registration.RealTeamId == match.AwayTeamId)
            orderby registration.RealTeamId == match.HomeTeamId descending, athlete.SportingName
            select new SheetAthlete(athlete.Id, registration.RealTeamId, athlete.SportingName, athlete.Position))
        .ToListAsync(cancellationToken);

    /// <summary>
    /// A súmula gravada, reconstruída como definição, para comparar com o que chega. Atleta
    /// sem participação gravada não aparece.
    /// </summary>
    public async Task<IReadOnlyList<MatchSheetAppearanceDefinition>> DefinitionsAsync(
        MatchSheet sheet,
        CancellationToken cancellationToken)
    {
        var appearances = await dbContext.AthleteAppearances
            .AsNoTracking()
            .Where(item => item.CompetitionId == sheet.CompetitionId && item.MatchSheetId == sheet.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var events = await dbContext.StatEvents
            .AsNoTracking()
            .Where(item => item.CompetitionId == sheet.CompetitionId && item.MatchSheetId == sheet.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byAthlete = events.ToLookup(item => item.AthleteId);

        return
        [
            .. appearances.Select(appearance =>
            {
                var own = byAthlete[appearance.AthleteId].ToDictionary(item => item.Type);
                int Quantity(StatEventType type) => own.GetValueOrDefault(type)?.Quantity ?? 0;
                return new MatchSheetAppearanceDefinition(
                    appearance.AthleteId,
                    appearance.RealTeamId,
                    appearance.Position,
                    appearance.DidPlay,
                    appearance.PlayedAsGoalkeeper,
                    appearance.GoalsConceded,
                    Quantity(StatEventType.Goal),
                    Quantity(StatEventType.Assist),
                    Quantity(StatEventType.GoalkeeperSave),
                    Quantity(StatEventType.PenaltySave),
                    Quantity(StatEventType.YellowCard),
                    Quantity(StatEventType.RedCard),
                    own.GetValueOrDefault(StatEventType.RedCard)?.RedCardReason,
                    Quantity(StatEventType.OwnGoal),
                    Quantity(StatEventType.PenaltyMiss));
            }),
        ];
    }

    /// <summary>Com um único goleiro no time, os gols sofridos dele são o placar adversário.</summary>
    public static void NormalizeSingleGoalkeeper(
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

    /// <summary>Apaga participações e eventos da súmula para gravar os novos no lugar.</summary>
    public async Task ReplaceDetailsAsync(MatchSheet sheet, CancellationToken cancellationToken)
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

    public void AddDetails(MatchSheet sheet, IEnumerable<MatchSheetAppearanceDefinition> definitions)
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
}
