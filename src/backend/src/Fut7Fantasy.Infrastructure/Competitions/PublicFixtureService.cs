using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Competitions;

/// <summary>
/// Calendário e súmulas públicas (Fase 8).
///
/// Fato publicado é público; fato em edição não é. O placar e a súmula só saem quando a
/// rodada está publicada — em conferência, ou reaberta para correção, a súmula está
/// sendo escrita, e mostrá-la publicaria meia edição como se fosse resultado.
/// </summary>
public sealed class PublicFixtureService(Fut7FantasyDbContext dbContext, TimeProvider clock)
    : IPublicFixtureService
{
    private const string GroupsNavigation = "_groups";


    public async Task<PublicFixturesView?> FixturesAsync(string slug, CancellationToken cancellationToken)
    {
        var competition = await PublishedAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var rounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(round => round.CompetitionId == competition.Id)
            .OrderBy(round => round.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rounds.Count == 0)
        {
            return new(competition.Slug!, competition.Name, competition.TimeZoneId, []);
        }

        var roundIds = rounds.ConvertAll(round => round.Id);
        var matches = await MatchRowsAsync(competition.Id, roundIds, cancellationToken).ConfigureAwait(false);
        var byRound = matches.ToLookup(row => row.Match.RoundId);
        var now = clock.GetUtcNow();

        return new(
            competition.Slug!,
            competition.Name,
            competition.TimeZoneId,
            [
                .. rounds.Select(round =>
                {
                    var phase = round.PhaseAt(now);
                    var publicado = round.Status == RoundStatus.Published;

                    // Publicada alguma vez e de volta à conferência: está em correção.
                    var emCorrecao = phase == RoundPhase.ReopenedForCorrection;
                    return new PublicRoundView(
                        round.Id,
                        round.Name,
                        round.Sequence,
                        phase.ToString(),
                        publicado,
                        emCorrecao,
                        phase == RoundPhase.Published,
                        [
                            .. byRound[round.Id]
                                .OrderBy(row => row.Match.KickoffAt)
                                .ThenBy(row => row.HomeTeamName)
                                .Select(row => new PublicFixtureView(
                                    row.Match.Id,
                                    row.StageName,
                                    row.HomeTeamName,
                                    row.AwayTeamName,
                                    row.Match.KickoffAt,
                                    CompetitionClock.ToLocalText(row.Match.KickoffAt, competition.TimeZoneId),
                                    row.Match.Status.ToString(),
                                    publicado ? row.HomeScore : null,
                                    publicado ? row.AwayScore : null,
                                    publicado && row.SheetId is not null)),
                        ]);
                }),
            ]);
    }

    public async Task<PublicMatchView?> MatchAsync(
        string slug,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var competition = await PublishedAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var row = (await MatchRowsAsync(competition.Id, null, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(item => item.Match.Id == matchId);
        if (row?.SheetId is not { } sheetId)
        {
            return null;
        }

        var round = await dbContext.Rounds
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == row.Match.RoundId, cancellationToken)
            .ConfigureAwait(false);

        // A partida existe, mas o resultado dela ainda não é público.
        if (round is null || round.Status != RoundStatus.Published)
        {
            return null;
        }

        var appearances = await dbContext.AthleteAppearances
            .AsNoTracking()
            .Where(item => item.MatchSheetId == sheetId && item.DidPlay)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var events = await dbContext.StatEvents
            .AsNoTracking()
            .Where(item => item.MatchSheetId == sheetId)
            .Select(item => new StatRow(item.AthleteId, item.Type, item.Quantity))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var athleteIds = appearances.ConvertAll(item => item.AthleteId).Distinct().ToList();
        var names = await dbContext.Athletes
            .AsNoTracking()
            .Where(athlete => athleteIds.Contains(athlete.Id))
            .Select(athlete => new { athlete.Id, athlete.SportingName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var porAtleta = events.ToLookup(item => item.AthleteId);
        var nomes = names.ToDictionary(item => item.Id, item => item.SportingName);

        PublicMatchTeamView Time(Guid teamId, string nome, bool mandante) => new(
            nome,
            mandante,
            [
                .. appearances
                    .Where(item => item.RealTeamId == teamId)
                    .Select(item => new PublicMatchAthleteView(
                        nomes.GetValueOrDefault(item.AthleteId, "Atleta"),
                        item.Position.ToString(),
                        item.PlayedAsGoalkeeper,
                        Total(porAtleta, item.AthleteId, StatEventType.Goal),
                        Total(porAtleta, item.AthleteId, StatEventType.Assist),
                        Total(porAtleta, item.AthleteId, StatEventType.GoalkeeperSave),
                        Total(porAtleta, item.AthleteId, StatEventType.PenaltySave),
                        Total(porAtleta, item.AthleteId, StatEventType.YellowCard),
                        Total(porAtleta, item.AthleteId, StatEventType.RedCard),
                        Total(porAtleta, item.AthleteId, StatEventType.OwnGoal),
                        Total(porAtleta, item.AthleteId, StatEventType.PenaltyMiss),
                        item.GoalsConceded))
                    // Quem fez mais primeiro; depois o nome, para a ordem não sortear.
                    .OrderByDescending(atleta => atleta.Goals + atleta.Assists)
                    .ThenBy(atleta => atleta.SportingName, StringComparer.Ordinal),
            ]);

        return new(
            row.Match.Id,
            competition.Slug!,
            competition.Name,
            round.Name,
            row.StageName,
            row.HomeTeamName,
            row.AwayTeamName,
            row.HomeScore ?? 0,
            row.AwayScore ?? 0,
            CompetitionClock.ToLocalText(row.Match.KickoffAt, competition.TimeZoneId),
            round.PhaseAt(clock.GetUtcNow()) == RoundPhase.Published,
            [
                Time(row.Match.HomeTeamId, row.HomeTeamName, true),
                Time(row.Match.AwayTeamId, row.AwayTeamName, false),
            ]);
    }

    public async Task<PublicStandingsView?> StandingsAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var competition = await PublishedAsync(slug, cancellationToken).ConfigureAwait(false);
        if (competition is null)
        {
            return null;
        }

        var stages = await dbContext.Stages
            .AsNoTracking()
            // `Groups` é propriedade computada sobre o campo privado; o EF só inclui a
            // navegação pelo nome do campo, como o serviço de fases já faz.
            .Include(GroupsNavigation)
            .Where(stage => stage.CompetitionId == competition.Id)
            .OrderBy(stage => stage.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (stages.Count == 0)
        {
            return new(competition.Slug!, competition.Name, []);
        }

        var participants = await dbContext.StageParticipants
            .AsNoTracking()
            .Where(item => stages.Select(stage => stage.Id).Contains(item.StageId))
            .Select(item => new ParticipantRow(item.StageId, item.RealTeamId, item.StageGroupId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var nomes = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => team.CompetitionId == competition.Id)
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken)
            .ConfigureAwait(false);

        // Só placar publicado entra, e partida adiada ou cancelada fica de fora.
        var resultados = await (
                from match in dbContext.Matches.AsNoTracking()
                where match.CompetitionId == competition.Id && match.Status == MatchStatus.Scheduled
                join round in dbContext.Rounds.AsNoTracking() on match.RoundId equals round.Id
                where round.Status == RoundStatus.Published
                join sheet in dbContext.MatchSheets.AsNoTracking() on match.Id equals sheet.MatchId
                select new
                {
                    match.StageId,
                    match.HomeTeamId,
                    match.AwayTeamId,
                    sheet.HomeScore,
                    sheet.AwayScore,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var porFase = resultados.ToLookup(item => item.StageId);

        return new(
            competition.Slug!,
            competition.Name,
            [
                .. stages.Select(stage =>
                {
                    var daFase = porFase[stage.Id]
                        .Select(item => new StandingsMatch(
                            item.HomeTeamId, item.HomeScore, item.AwayTeamId, item.AwayScore))
                        .ToList();
                    var naFase = participants.Where(item => item.StageId == stage.Id).ToList();

                    // Mata-mata não tem tabela: a fase vem declarada e sem grupo nenhum.
                    var grupos = stage.Format != StageFormat.Groups
                        ? []
                        : Tabelas(stage, naFase, daFase, nomes);

                    return new PublicStageStandingsView(
                        stage.Name,
                        stage.Sequence,
                        stage.Format.ToString(),
                        [.. stage.Tiebreakers.Select(criterio => criterio.ToString())],
                        grupos);
                }),
            ]);
    }

    /// <summary>
    /// Uma tabela por grupo; sem grupos, uma só com todo mundo da fase. Cada tabela conta
    /// apenas os jogos entre os times dela, que é o que faz um grupo ser um grupo.
    /// </summary>
    private static List<PublicGroupStandingsView> Tabelas(
        Stage stage,
        IReadOnlyList<ParticipantRow> naFase,
        IReadOnlyList<StandingsMatch> daFase,
        IReadOnlyDictionary<Guid, string> nomes)
    {
        List<(string? Nome, List<Guid> Times)> blocos = stage.Groups.Count > 0
            ? [.. stage.Groups.Select(grupo => (
                (string?)grupo.Name,
                naFase
                    .Where(item => item.StageGroupId == grupo.Id)
                    .Select(item => item.RealTeamId)
                    .ToList()))]
            : [(null, naFase.Select(item => item.RealTeamId).ToList())];

        return
        [
            .. blocos.Select(bloco =>
            {
                var doBloco = bloco.Times.ToHashSet();
                var jogos = daFase
                    .Where(jogo => doBloco.Contains(jogo.HomeTeamId) && doBloco.Contains(jogo.AwayTeamId))
                    .ToList();
                var linhas = GroupStandings.Order(
                    GroupStandings.Build(bloco.Times, jogos),
                    jogos,
                    stage.Tiebreakers);

                return new PublicGroupStandingsView(
                    bloco.Nome,
                    [
                        .. linhas.Select(linha => new PublicStandingsRowView(
                            linha.Position,
                            linha.Tied,
                            linha.Record.TeamId,
                            nomes.GetValueOrDefault(linha.Record.TeamId, "Time"),
                            linha.Record.Played,
                            linha.Record.Wins,
                            linha.Record.Draws,
                            linha.Record.Losses,
                            linha.Record.GoalsFor,
                            linha.Record.GoalsAgainst,
                            linha.Record.GoalDifference,
                            linha.Record.Points)),
                    ]);
            }),
        ];
    }

    private static int Total(ILookup<Guid, StatRow> events, Guid athleteId, StatEventType type) =>
        events[athleteId].Where(item => item.Type == type).Sum(item => item.Quantity);

    private Task<Competition?> PublishedAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Slug == slug && item.Status == CompetitionStatus.Published,
                cancellationToken);

    /// <summary>
    /// Partidas com os nomes que a tela precisa, já resolvidos. Sem <paramref name="roundIds"/>
    /// traz as do campeonato inteiro.
    /// </summary>
    private async Task<List<MatchRow>> MatchRowsAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid>? roundIds,
        CancellationToken cancellationToken)
    {
        // O filtro de rodadas vem antes da projeção: depois dela o EF não traduz o
        // `Contains` sobre o campo aninhado e a consulta inteira cai para o cliente.
        var matches = dbContext.Matches
            .AsNoTracking()
            .Where(match => match.CompetitionId == competitionId);
        if (roundIds is not null)
        {
            matches = matches.Where(match => roundIds.Contains(match.RoundId));
        }

        var query =
            from match in matches
            join stage in dbContext.Stages.AsNoTracking() on match.StageId equals stage.Id
            join home in dbContext.RealTeams.AsNoTracking() on match.HomeTeamId equals home.Id
            join away in dbContext.RealTeams.AsNoTracking() on match.AwayTeamId equals away.Id
            join sheet in dbContext.MatchSheets.AsNoTracking() on match.Id equals sheet.MatchId into sheets
            from sheet in sheets.DefaultIfEmpty()
            select new MatchRow(
                match,
                stage.Name,
                home.Name,
                away.Name,
                sheet != null ? sheet.HomeScore : null,
                sheet != null ? sheet.AwayScore : null,
                sheet != null ? sheet.Id : null);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record StatRow(Guid AthleteId, StatEventType Type, int Quantity);

    private sealed record ParticipantRow(Guid StageId, Guid RealTeamId, Guid? StageGroupId);

    private sealed record MatchRow(
        Match Match,
        string StageName,
        string HomeTeamName,
        string AwayTeamName,
        int? HomeScore,
        int? AwayScore,
        Guid? SheetId);
}
