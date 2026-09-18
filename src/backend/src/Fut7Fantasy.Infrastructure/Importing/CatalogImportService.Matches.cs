using Fut7Fantasy.Application.Importing;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.Importing;
using Fut7Fantasy.Domain.SportsCatalog;
using Microsoft.EntityFrameworkCore;

namespace Fut7Fantasy.Infrastructure.Importing;

/// <summary>
/// Importação de partidas. Aplica as mesmas regras do cadastro pela tela: rodada em
/// rascunho, fase do campeonato, times confirmados nela e, em grupos, no mesmo grupo.
/// </summary>
public sealed partial class CatalogImportService
{
    private async Task<ImportResult> MatchesAsync(
        Guid competitionId,
        CsvDocument document,
        bool apply,
        CancellationToken cancellationToken)
    {
        // Linhas com erro de formato ficam de fora, mas as outras continuam sendo conferidas
        // contra o campeonato: a pessoa vê todos os problemas do arquivo de uma vez.
        var parsed = ImportParser.Matches(document);

        var timeZoneId = await dbContext.Competitions
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.TimeZoneId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var rounds = await dbContext.Rounds
            .Where(round => round.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(stage => stage.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stageIds = stages.Select(stage => stage.Id).ToList();
        var participants = await dbContext.StageParticipants
            .AsNoTracking()
            .Where(participant => stageIds.Contains(participant.StageId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var teams = await dbContext.RealTeams
            .AsNoTracking()
            .Where(team => team.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var matches = await dbContext.Matches
            .Where(match => match.CompetitionId == competitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roundsByName = rounds.ToLookup(round => round.Name, StringComparer.OrdinalIgnoreCase);
        var stagesByName = stages.ToLookup(stage => stage.Name, StringComparer.OrdinalIgnoreCase);
        var teamsByName = teams.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);

        List<ImportIssue> issues = [.. parsed.Issues];
        List<string> newRounds = [];
        List<(MatchImportRow Row, MatchDefinition Definition)> created = [];
        List<(Match Match, MatchDefinition Definition)> updated = [];
        HashSet<Round> touched = [];
        var unchanged = 0;

        foreach (var row in parsed.Rows)
        {
            var before = issues.Count;
            var round = RoundOf(row, roundsByName, issues);
            var stage = One(stagesByName[row.StageName], row.StageName, row.Line, "fase", issues);
            var home = TeamOf(row.HomeTeamName, "mandante", row, teamsByName, issues);
            var away = TeamOf(row.AwayTeamName, "visitante", row, teamsByName, issues);

            if (!CompetitionClock.TryToUtc(row.KickoffLocal, timeZoneId, out var kickoff, out var failure))
            {
                issues.Add(new(row.Line, "hora", failure == LocalTimeFailure.DoesNotExist
                    ? "Esse horário não existe no fuso do campeonato, por causa do horário de verão."
                    : "O fuso do campeonato não é reconhecido pelo servidor."));
            }

            if (issues.Count > before || stage is null || home is null || away is null)
            {
                continue;
            }

            if (Eligibility(stage, home, away, participants) is { } problem)
            {
                issues.Add(new(row.Line, problem.Column, problem.Message));
                continue;
            }

            var definition = new MatchDefinition(stage.Id, home.Id, away.Id, kickoff);
            if (round is null)
            {
                if (!newRounds.Contains(row.RoundName, StringComparer.OrdinalIgnoreCase))
                {
                    newRounds.Add(row.RoundName);
                }

                created.Add((row, definition));
                continue;
            }

            var existing = matches
                .Where(match => match.RoundId == round.Id
                    && match.HomeTeamId == home.Id
                    && match.AwayTeamId == away.Id)
                .ToList();
            switch (existing)
            {
                case []:
                    created.Add((row, definition));
                    touched.Add(round);
                    break;
                case [var match] when match.StageId == stage.Id && match.KickoffAt == kickoff:
                    unchanged++;
                    break;
                case [var match]:
                    updated.Add((match, definition));
                    touched.Add(round);
                    break;
                default:
                    issues.Add(new(
                        row.Line,
                        "mandante",
                        $"{row.RoundName} já tem mais de um jogo entre {home.Name} e {away.Name}. "
                        + "Ajuste esses jogos pela tela."));
                    break;
            }
        }

        if (rounds.Count + newRounds.Count > Round.MaxRoundsPerCompetition)
        {
            issues.Add(new(
                ImportParser.HeaderLine,
                "rodada",
                $"O campeonato comporta até {Round.MaxRoundsPerCompetition} rodadas, e o arquivo "
                + $"criaria {newRounds.Count} além das {rounds.Count} existentes."));
        }

        if (issues.Count > 0)
        {
            return ImportResult.Invalid(issues.OrderBy(issue => issue.Line));
        }

        var summary = new ImportSummary(parsed.Rows.Count, created.Count, updated.Count, unchanged);
        if (!apply)
        {
            return Completed(summary) with { Notes = RoundNotes(newRounds, preview: true) };
        }

        var now = clock.GetUtcNow();
        var createdRounds = newRounds
            .Select((name, index) => Round.Create(
                Guid.CreateVersion7(), competitionId, rounds.Count + index + 1, new RoundDefinition(name), now))
            .ToDictionary(round => round.Name, StringComparer.OrdinalIgnoreCase);
        dbContext.Rounds.AddRange(createdRounds.Values);

        foreach (var (row, definition) in created)
        {
            var roundId = createdRounds.TryGetValue(row.RoundName, out var fresh)
                ? fresh.Id
                : roundsByName[row.RoundName].Single().Id;
            dbContext.Matches.Add(Match.Create(Guid.CreateVersion7(), competitionId, roundId, definition, now));
        }

        foreach (var (match, definition) in updated)
        {
            match.Update(definition, now);
        }

        // Mexer só nas partidas não altera a linha da rodada; forçar o UPDATE faz a versão
        // avançar, e a tela aberta com a versão antiga recebe conflito em vez de sobrescrever.
        foreach (var round in touched)
        {
            dbContext.Entry(round).Property(item => item.UpdatedAt).IsModified = true;
        }

        return await SaveAsync(
                competitionId,
                ImportKind.Matches,
                summary,
                now,
                cancellationToken,
                RoundNotes(newRounds, preview: false))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rodada existente só recebe partidas em rascunho, como pela tela. A que não existe
    /// fica null e será criada; o nome já foi validado pelo leitor.
    /// </summary>
    private static Round? RoundOf(
        MatchImportRow row,
        ILookup<string, Round> roundsByName,
        List<ImportIssue> issues)
    {
        var candidates = roundsByName[row.RoundName].ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var round = One(candidates, row.RoundName, row.Line, "rodada", issues);
        if (round is not null && !round.AcceptsMatchChanges)
        {
            issues.Add(new(
                row.Line,
                "rodada",
                $"{round.Name} já saiu do rascunho. Partidas só entram por arquivo com a rodada em rascunho; "
                + "remarque, adie ou cancele pela tela."));
            return null;
        }

        return round;
    }

    /// <summary>Nome que não existe ou que dois registros compartilham não aponta para nada.</summary>
    private static T? One<T>(
        IEnumerable<T> candidates,
        string name,
        int line,
        string column,
        List<ImportIssue> issues)
        where T : class
    {
        switch (candidates.ToList())
        {
            case [var single]:
                return single;
            case []:
                issues.Add(new(line, column, $"Não existe {column} chamada `{name}` neste campeonato."));
                return null;
            default:
                issues.Add(new(
                    line,
                    column,
                    $"Há mais de uma {column} chamada `{name}`. Renomeie uma delas pela tela."));
                return null;
        }
    }

    private static RealTeam? TeamOf(
        string name,
        string column,
        MatchImportRow row,
        Dictionary<string, RealTeam> teamsByName,
        List<ImportIssue> issues)
    {
        if (!teamsByName.TryGetValue(name, out var team))
        {
            issues.Add(new(row.Line, column, $"Não existe time chamado `{name}` neste campeonato."));
            return null;
        }

        if (team.IsArchived)
        {
            issues.Add(new(row.Line, column, $"`{team.Name}` está arquivado e não joga."));
            return null;
        }

        return team;
    }

    /// <summary>A mesma regra do cadastro pela tela, com a mensagem apontando a coluna.</summary>
    private static (string Column, string Message)? Eligibility(
        Stage stage,
        RealTeam home,
        RealTeam away,
        List<StageParticipant> participants)
    {
        var homeParticipant = participants.SingleOrDefault(
            item => item.StageId == stage.Id && item.RealTeamId == home.Id);
        var awayParticipant = participants.SingleOrDefault(
            item => item.StageId == stage.Id && item.RealTeamId == away.Id);
        if (homeParticipant is null)
        {
            return ("mandante", $"`{home.Name}` não está confirmado em {stage.Name}.");
        }

        if (awayParticipant is null)
        {
            return ("visitante", $"`{away.Name}` não está confirmado em {stage.Name}.");
        }

        return stage.Format == StageFormat.Groups && homeParticipant.StageGroupId != awayParticipant.StageGroupId
            ? ("visitante", $"Em {stage.Name}, {home.Name} e {away.Name} estão em grupos diferentes.")
            : null;
    }

    private static IReadOnlyList<string> RoundNotes(List<string> newRounds, bool preview) =>
        newRounds.Count == 0
            ? []
            :
            [
                $"{(preview ? "Rodadas a criar" : "Rodadas criadas")} em rascunho, no fim da ordem: "
                + $"{string.Join(", ", newRounds)}.",
            ];
}
