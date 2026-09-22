using System.Globalization;
using Fut7Fantasy.Application.Abstractions;
using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Importing;
using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Competitions;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Importing;

public sealed class RoundStatisticsImportService(
    Fut7FantasyDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider clock) : IRoundStatisticsImportService
{
    private readonly MatchSheetStore _store = new(dbContext);

    public async Task<RoundStatisticsFile?> TemplateAsync(
        Guid competitionId,
        Guid roundId,
        CancellationToken cancellationToken)
    {
        var round = await RoundAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false);
        if (round is null)
        {
            return null;
        }

        List<IReadOnlyList<string>> rows = [];
        foreach (var game in await GamesAsync(round, cancellationToken).ConfigureAwait(false))
        {
            var roster = await _store.RosterAsync(game.Match, cancellationToken).ConfigureAwait(false);
            var sheet = await SheetAsync(game.Match, tracked: false, cancellationToken).ConfigureAwait(false);
            var current = sheet is null
                ? new Dictionary<Guid, MatchSheetAppearanceDefinition>()
                : (await _store.DefinitionsAsync(sheet, cancellationToken).ConfigureAwait(false))
                    .ToDictionary(item => item.AthleteId);

            // O que já foi lançado vem preenchido: baixar, corrigir e reenviar é o caminho
            // de correção, sem precisar redigitar a súmula inteira.
            rows.AddRange(roster.Select(athlete => Row(game, athlete, current.GetValueOrDefault(athlete.AthleteId))));
        }

        var template = ImportTemplates.For(ImportKind.MatchStatistics);
        return new(
            $"estatisticas-v{template.Version}-rodada-{round.Sequence}.csv",
            template.Render(rows));
    }

    public Task<ImportResult> PreviewAsync(
        Guid competitionId,
        Guid roundId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, roundId, content, apply: false, cancellationToken);

    public Task<ImportResult> CommitAsync(
        Guid competitionId,
        Guid roundId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        RunAsync(competitionId, roundId, content, apply: true, cancellationToken);

    /// <summary>Conferir e importar percorrem o mesmo caminho; só o último passo difere (ADR-009).</summary>
    private async Task<ImportResult> RunAsync(
        Guid competitionId,
        Guid roundId,
        ReadOnlyMemory<byte> content,
        bool apply,
        CancellationToken cancellationToken)
    {
        var read = CsvReader.Read(content.Span);
        if (read.Document is null)
        {
            return ImportResult.Rejected(CatalogImportService.Explain(read.Failure!.Value, read.Line));
        }

        if (await RoundAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ImportResult.Of(ImportOutcome.NotFound);
        }

        return await CompetitionLock.RunAsync(
            dbContext,
            competitionId,
            async () =>
            {
                // Relida dentro da trava: o estado que vale é o do momento da gravação.
                var round = await RoundAsync(competitionId, roundId, cancellationToken).ConfigureAwait(false);
                return round is null
                    ? ImportResult.Of(ImportOutcome.NotFound)
                    : await ImportAsync(round, read.Document, apply, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportResult> ImportAsync(
        Round round,
        CsvDocument document,
        bool apply,
        CancellationToken cancellationToken)
    {
        // Linhas com erro de formato ficam de fora, mas as outras continuam sendo conferidas.
        var parsed = ImportParser.MatchStatistics(document);
        List<ImportIssue> issues = [.. parsed.Issues];
        var now = clock.GetUtcNow();
        var phase = round.PhaseAt(now);
        var games = await GamesAsync(round, cancellationToken).ConfigureAwait(false);
        var gamesByPair = games.ToLookup(game => Pair(game.HomeName, game.AwayName));
        List<SheetPlan> plans = [];

        var groups = parsed.Rows
            .GroupBy(row => Pair(row.HomeTeamName, row.AwayTeamName))
            .OrderBy(group => group.Min(row => row.Line));
        foreach (var group in groups)
        {
            var first = group.MinBy(row => row.Line)!;
            var label = $"{first.HomeTeamName} x {first.AwayTeamName}";
            switch (gamesByPair[group.Key].ToList())
            {
                case []:
                    issues.Add(new(first.Line, "mandante", $"{label} não é um jogo marcado de {round.Name}."));
                    continue;
                case [var game]:
                    if (!RoundPhases.AcceptsSheetChanges(phase))
                    {
                        issues.Add(new(
                            first.Line,
                            "mandante",
                            $"{round.Name} não recebe súmula agora: só depois do início dos jogos "
                            + "e antes da publicação."));
                        continue;
                    }

                    if (now < game.Match.KickoffAt)
                    {
                        issues.Add(new(first.Line, "mandante", $"{label} ainda não começou."));
                        continue;
                    }

                    if (await PlanAsync(game, [.. group], issues, cancellationToken).ConfigureAwait(false) is { } plan)
                    {
                        plans.Add(plan);
                    }

                    continue;
                default:
                    issues.Add(new(
                        first.Line,
                        "mandante",
                        $"{round.Name} tem mais de um jogo {label}. Lance essas súmulas pela tela."));
                    continue;
            }
        }

        if (issues.Count > 0)
        {
            return ImportResult.Invalid(issues.OrderBy(issue => issue.Line));
        }

        var summary = new ImportSummary(
            parsed.Rows.Count,
            plans.Count(plan => plan.Existing is null),
            plans.Count(plan => plan.Existing is not null && !plan.Unchanged),
            plans.Count(plan => plan.Unchanged));
        var notes = Notes(round, games, plans, apply);
        if (!apply)
        {
            return new ImportResult(ImportOutcome.Completed, summary, [], null) { Notes = notes };
        }

        foreach (var plan in plans.Where(plan => !plan.Unchanged))
        {
            var sheet = plan.Existing;
            if (sheet is null)
            {
                sheet = MatchSheet.Create(
                    Guid.CreateVersion7(),
                    round.CompetitionId,
                    plan.Game.Match.Id,
                    plan.HomeScore,
                    plan.AwayScore,
                    now);
                dbContext.MatchSheets.Add(sheet);
            }
            else
            {
                sheet.UpdateScore(plan.HomeScore, plan.AwayScore, now);
                await _store.ReplaceDetailsAsync(sheet, cancellationToken).ConfigureAwait(false);
            }

            _store.AddDetails(sheet, plan.Definitions);
            Audit("CompetitionMatchSheetImported", sheet.Id, "Súmula da partida importada por CSV.", now);
        }

        // A auditoria guarda o resumo, nunca o conteúdo do arquivo (04 §9).
        Audit(
            "RoundStatisticsImported",
            round.Id,
            $"Importação de estatísticas: {summary.Created} súmulas criadas, {summary.Updated} substituídas, "
            + $"{summary.Unchanged} sem mudança.",
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Inclui a concorrência: alguém salvou uma súmula pela tela no meio da importação.
            return ImportResult.Rejected(
                "Outra pessoa salvou uma súmula desta rodada enquanto o arquivo era importado. "
                + "Nada foi gravado; confira o arquivo de novo.");
        }

        return new ImportResult(ImportOutcome.Completed, summary, [], null) { Notes = notes };
    }

    /// <summary>
    /// Monta a súmula de um jogo a partir das linhas dele. Atleta do elenco fora do arquivo
    /// entra como quem não jogou, que é o que a ausência na planilha quer dizer.
    /// </summary>
    private async Task<SheetPlan?> PlanAsync(
        Game game,
        IReadOnlyList<MatchStatisticsImportRow> rows,
        List<ImportIssue> issues,
        CancellationToken cancellationToken)
    {
        var match = game.Match;
        var roster = await _store.RosterAsync(match, cancellationToken).ConfigureAwait(false);
        var rosterByName = roster.ToDictionary(athlete => athlete.SportingName, StringComparer.OrdinalIgnoreCase);
        var label = $"{game.HomeName} x {game.AwayName}";
        var before = issues.Count;
        Dictionary<Guid, MatchSheetAppearanceDefinition> informed = [];
        HashSet<Guid> teamsWithInformedConceded = [];

        foreach (var row in rows)
        {
            if (!rosterByName.TryGetValue(row.AthleteName, out var athlete))
            {
                issues.Add(new(
                    row.Line,
                    "atleta",
                    $"`{row.AthleteName}` não está no elenco de {game.HomeName} nem de {game.AwayName} neste jogo."));
                continue;
            }

            var teamName = athlete.RealTeamId == match.HomeTeamId ? game.HomeName : game.AwayName;
            if (row.TeamName is not null && !string.Equals(row.TeamName, teamName, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new(row.Line, "time", $"`{athlete.SportingName}` é do {teamName}, não do {row.TeamName}."));
                continue;
            }

            var definition = new MatchSheetAppearanceDefinition(
                athlete.AthleteId,
                athlete.RealTeamId,
                athlete.Position,
                row.DidPlay,
                row.PlayedAsGoalkeeper,
                row.GoalsConceded ?? 0,
                row.Goals,
                row.Assists,
                row.GoalkeeperSaves,
                row.PenaltySaves,
                row.YellowCards,
                row.RedCard is null ? 0 : 1,
                row.RedCard,
                row.OwnGoals,
                row.PenaltyMisses);
            foreach (var error in definition.Validate())
            {
                issues.Add(new(row.Line, ImportParser.StatisticsColumnFor(error.Field), error.Message));
            }

            if (row.GoalsConceded is not null && row.PlayedAsGoalkeeper)
            {
                teamsWithInformedConceded.Add(athlete.RealTeamId);
            }

            informed[athlete.AthleteId] = definition;
        }

        if (issues.Count > before)
        {
            return null;
        }

        var definitions = roster
            .Select(athlete => informed.GetValueOrDefault(athlete.AthleteId) ?? Absent(athlete))
            .ToList();

        // O placar sai dos gols do time e dos gols contra do adversário, a mesma conta
        // que o editor confere; aqui ela é a fonte, e a prévia mostra o resultado.
        var homeScore = Score(definitions, match.HomeTeamId);
        var awayScore = Score(definitions, match.AwayTeamId);

        // Vazio em gols sofridos quer dizer "calcular", e só dá para calcular com um goleiro.
        if (!teamsWithInformedConceded.Contains(match.HomeTeamId))
        {
            MatchSheetStore.NormalizeSingleGoalkeeper(definitions, match.HomeTeamId, awayScore);
        }

        if (!teamsWithInformedConceded.Contains(match.AwayTeamId))
        {
            MatchSheetStore.NormalizeSingleGoalkeeper(definitions, match.AwayTeamId, homeScore);
        }

        var sheetErrors = new MatchSheetDefinition(homeScore, awayScore, definitions)
            .Validate(match.HomeTeamId, match.AwayTeamId);
        if (sheetErrors.Count > 0)
        {
            var line = rows.Min(row => row.Line);
            issues.AddRange(sheetErrors.Select(
                error => new ImportIssue(line, "mandante", $"{label}: {error.Message}")));
            return null;
        }

        var existing = await SheetAsync(match, tracked: true, cancellationToken).ConfigureAwait(false);
        var unchanged = existing is not null
            && existing.HomeScore == homeScore
            && existing.AwayScore == awayScore
            && Same(await _store.DefinitionsAsync(existing, cancellationToken).ConfigureAwait(false), definitions);
        return new(game, homeScore, awayScore, definitions, existing, unchanged);
    }

    private static int Score(List<MatchSheetAppearanceDefinition> definitions, Guid teamId) =>
        definitions.Where(item => item.RealTeamId == teamId).Sum(item => item.Goals)
        + definitions.Where(item => item.RealTeamId != teamId).Sum(item => item.OwnGoals);

    private static bool Same(
        IReadOnlyList<MatchSheetAppearanceDefinition> stored,
        List<MatchSheetAppearanceDefinition> incoming) =>
        stored.Count == incoming.Count && stored.ToHashSet().SetEquals(incoming);

    private static MatchSheetAppearanceDefinition Absent(SheetAthlete athlete) =>
        new(athlete.AthleteId, athlete.RealTeamId, athlete.Position, false, false, 0, 0, 0, 0, 0, 0, 0, null, 0, 0);

    /// <summary>O placar de cada jogo em destaque, que é o que a pessoa confere antes de importar.</summary>
    private static List<string> Notes(Round round, List<Game> games, List<SheetPlan> plans, bool apply)
    {
        List<string> notes =
        [
            .. plans.Select(plan =>
            {
                var score = $"{plan.Game.HomeName} {plan.HomeScore} x {plan.AwayScore} {plan.Game.AwayName}";
                return plan switch
                {
                    { Unchanged: true } => $"{score}: igual à súmula lançada.",
                    { Existing: not null } => apply
                        ? $"{score}: súmula lançada substituída."
                        : $"{score}: substitui a súmula já lançada.",
                    _ => $"{score}: súmula nova.",
                };
            }),
        ];

        var outside = games.Count(game => plans.All(plan => plan.Game.Match.Id != game.Match.Id));
        if (outside > 0)
        {
            notes.Add(outside == 1
                ? $"1 jogo de {round.Name} não está no arquivo e continua como está."
                : $"{outside} jogos de {round.Name} não estão no arquivo e continuam como estão.");
        }

        return notes;
    }

    private static List<string> Row(Game game, SheetAthlete athlete, MatchSheetAppearanceDefinition? current)
    {
        static string Count(int? value) =>
            value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        return
        [
            game.HomeName,
            game.AwayName,
            athlete.SportingName,
            athlete.RealTeamId == game.Match.HomeTeamId ? game.HomeName : game.AwayName,
            current?.DidPlay == true ? "sim" : "nao",
            current?.PlayedAsGoalkeeper == true ? "sim" : "nao",
            current?.PlayedAsGoalkeeper == true
                ? current.GoalsConceded.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            Count(current?.Goals),
            Count(current?.Assists),
            Count(current?.GoalkeeperSaves),
            Count(current?.PenaltySaves),
            Count(current?.YellowCards),
            current?.RedCardReason switch
            {
                RedCardReason.Direct => "direto",
                RedCardReason.SecondYellow => "segundo amarelo",
                _ => string.Empty,
            },
            Count(current?.OwnGoals),
            Count(current?.PenaltyMisses),
        ];
    }

    private void Audit(string action, Guid targetId, string summary, DateTimeOffset now) =>
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            currentUser.Id ?? throw new InvalidOperationException("O caso de uso exige uma conta autenticada."),
            action,
            targetId,
            summary,
            now));

    private Task<Round?> RoundAsync(Guid competitionId, Guid roundId, CancellationToken cancellationToken) =>
        dbContext.Rounds
            .AsNoTracking()
            .SingleOrDefaultAsync(
                round => round.Id == roundId && round.CompetitionId == competitionId,
                cancellationToken);

    /// <summary>Só jogo marcado tem súmula: adiado e cancelado saem da apuração da rodada.</summary>
    private Task<List<Game>> GamesAsync(Round round, CancellationToken cancellationToken) =>
        (
            from match in dbContext.Matches.AsNoTracking()
            join home in dbContext.RealTeams.AsNoTracking() on match.HomeTeamId equals home.Id
            join away in dbContext.RealTeams.AsNoTracking() on match.AwayTeamId equals away.Id
            where match.CompetitionId == round.CompetitionId
                && match.RoundId == round.Id
                && match.Status == MatchStatus.Scheduled
            orderby match.KickoffAt
            select new Game(match, home.Name, away.Name))
        .ToListAsync(cancellationToken);

    private Task<MatchSheet?> SheetAsync(Match match, bool tracked, CancellationToken cancellationToken)
    {
        var sheets = tracked ? dbContext.MatchSheets : dbContext.MatchSheets.AsNoTracking();
        return sheets.SingleOrDefaultAsync(
            sheet => sheet.CompetitionId == match.CompetitionId && sheet.MatchId == match.Id,
            cancellationToken);
    }

    /// <summary>Chave do jogo pelos nomes, sem diferença de maiúsculas.</summary>
    private static (string Home, string Away) Pair(string home, string away) =>
        (home.ToUpperInvariant(), away.ToUpperInvariant());

    private sealed record Game(Match Match, string HomeName, string AwayName);

    private sealed record SheetPlan(
        Game Game,
        int HomeScore,
        int AwayScore,
        List<MatchSheetAppearanceDefinition> Definitions,
        MatchSheet? Existing,
        bool Unchanged);
}
