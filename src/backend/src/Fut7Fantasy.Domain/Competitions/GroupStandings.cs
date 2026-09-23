namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Um resultado que conta para a classificação: dois times e o placar final.
///
/// Partida adiada ou cancelada não vira <see cref="StandingsMatch"/> — ela sai da conta
/// (decisão do Pedro em 2026-09-23), e partida sem resultado publicado também não entra,
/// pela mesma regra do calendário: fato em edição não é fato público.
/// </summary>
public sealed record StandingsMatch(Guid HomeTeamId, int HomeScore, Guid AwayTeamId, int AwayScore);

/// <summary>
/// A campanha de um time. Pontos são vitória 3, empate 1 e derrota 0 (01 §7), e derivam
/// dos jogos em vez de serem guardados, para que corrigir uma súmula corrija a tabela.
/// </summary>
public sealed record TeamRecord(
    Guid TeamId,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst)
{
    public int Points => (Wins * 3) + Draws;

    public int GoalDifference => GoalsFor - GoalsAgainst;
}

/// <summary>Uma linha da tabela; empatados dividem a colocação.</summary>
public sealed record StandingsRow(int Position, bool Tied, TeamRecord Record);

/// <summary>
/// A classificação de um grupo de pontos corridos (01 §7).
///
/// O nome diz "grupo" para não se confundir com a classificação do fantasy, que vive em
/// `Infrastructure.Scoring.Standings` e ordena participações, não times reais.
///
/// Só existe em fase de <see cref="StageFormat.Groups"/>: mata-mata não tem tabela, e um
/// campeonato pequeno pode ser só mata-mata — o organizador escolhe o formato conforme a
/// quantidade de times (decisão do Pedro em 2026-09-23).
///
/// Depois dos pontos, a ordem é a que a organização escolheu em <c>Stage.Tiebreakers</c>.
/// Nenhum critério é embutido aqui além dos pontos: a várzea varia, e quem manda é quem
/// organiza. Esgotados os critérios, o empate persiste e as linhas dividem a colocação.
/// </summary>
public static class GroupStandings
{
    /// <summary>
    /// A campanha de cada time a partir dos jogos. Times sem jogo entram zerados, porque
    /// estar na fase e ainda não ter jogado é diferente de não estar nela.
    /// </summary>
    public static IReadOnlyList<TeamRecord> Build(
        IEnumerable<Guid> teamIds,
        IEnumerable<StandingsMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(teamIds);
        ArgumentNullException.ThrowIfNull(matches);

        var jogos = matches.ToList();
        return
        [
            .. teamIds.Distinct().Select(teamId =>
            {
                var played = 0;
                var wins = 0;
                var draws = 0;
                var losses = 0;
                var goalsFor = 0;
                var goalsAgainst = 0;

                foreach (var jogo in jogos)
                {
                    var casa = jogo.HomeTeamId == teamId;
                    if (!casa && jogo.AwayTeamId != teamId)
                    {
                        continue;
                    }

                    var pro = casa ? jogo.HomeScore : jogo.AwayScore;
                    var contra = casa ? jogo.AwayScore : jogo.HomeScore;
                    played++;
                    goalsFor += pro;
                    goalsAgainst += contra;
                    if (pro > contra)
                    {
                        wins++;
                    }
                    else if (pro == contra)
                    {
                        draws++;
                    }
                    else
                    {
                        losses++;
                    }
                }

                return new TeamRecord(teamId, played, wins, draws, losses, goalsFor, goalsAgainst);
            }),
        ];
    }

    /// <summary>
    /// A tabela ordenada. As mesmas campanhas com os mesmos critérios dão sempre a mesma
    /// lista: o identificador do time fecha a ordem, para que nada dependa de sorteio.
    /// </summary>
    public static IReadOnlyList<StandingsRow> Order(
        IEnumerable<TeamRecord> records,
        IEnumerable<StandingsMatch> matches,
        IReadOnlyList<TiebreakCriterion> tiebreakers)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(tiebreakers);

        var jogos = matches.ToList();
        var todos = records.ToList();

        // A comparação é par a par porque o confronto direto depende do conjunto de
        // empatados, e não de um valor fixo por time. O conjunto usado é o de antes da
        // ordenação, para que o critério não mude enquanto a lista se reorganiza.
        //
        // A ordenação usa `OrderBy`, e não `List.Sort`: o confronto direto pode ser
        // circular — A ganha de B, B de C e C de A —, e um comparador não transitivo faz
        // o `Sort` do .NET lançar exceção. O `OrderBy` é estável e não reclama; num ciclo
        // assim a ordem fica decidida pelo identificador, que é o último critério.
        var comparador = Comparer<TeamRecord>.Create(
            (left, right) => Compare(left, right, jogos, tiebreakers, todos));
        var ordered = todos.OrderBy(record => record, comparador).ToList();

        List<StandingsRow> rows = [];
        var position = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            var record = ordered[index];
            var tiedWithPrevious =
                index > 0 && Undecided(ordered[index - 1], record, jogos, tiebreakers, todos);
            if (!tiedWithPrevious)
            {
                // Colocação de competição: depois de dois segundos lugares vem o quarto.
                position = index + 1;
            }

            var tiedWithNext =
                index + 1 < ordered.Count
                && Undecided(record, ordered[index + 1], jogos, tiebreakers, todos);
            rows.Add(new(position, tiedWithPrevious || tiedWithNext, record));
        }

        return rows;
    }

    private static int Compare(
        TeamRecord left,
        TeamRecord right,
        IReadOnlyList<StandingsMatch> matches,
        IReadOnlyList<TiebreakCriterion> tiebreakers,
        IReadOnlyList<TeamRecord> all)
    {
        if (left.Points != right.Points)
        {
            return right.Points.CompareTo(left.Points);
        }

        foreach (var criterion in tiebreakers)
        {
            var decided = Apply(criterion, left, right, matches, all);
            if (decided != 0)
            {
                return decided;
            }
        }

        return left.TeamId.CompareTo(right.TeamId);
    }

    /// <summary>Empatado é não ter sido decidido por nenhum critério escolhido.</summary>
    private static bool Undecided(
        TeamRecord left,
        TeamRecord right,
        IReadOnlyList<StandingsMatch> matches,
        IReadOnlyList<TiebreakCriterion> tiebreakers,
        IReadOnlyList<TeamRecord> all)
    {
        if (left.Points != right.Points)
        {
            return false;
        }

        return tiebreakers.All(criterion => Apply(criterion, left, right, matches, all) == 0);
    }

    private static int Apply(
        TiebreakCriterion criterion,
        TeamRecord left,
        TeamRecord right,
        IReadOnlyList<StandingsMatch> matches,
        IReadOnlyList<TeamRecord> all) => criterion switch
        {
            TiebreakCriterion.Wins => right.Wins.CompareTo(left.Wins),
            TiebreakCriterion.GoalDifference => right.GoalDifference.CompareTo(left.GoalDifference),
            TiebreakCriterion.GoalsFor => right.GoalsFor.CompareTo(left.GoalsFor),
            TiebreakCriterion.HeadToHead => HeadToHead(left, right, matches, all),

            // Cartões ainda não chegam à tabela: eles vivem na súmula do fantasy, e trazê-los
            // para cá exigiria somá-los por time real. Fica registrado no roadmap.
            _ => 0,
        };

    /// <summary>
    /// Confronto direto entre os times que empataram em pontos, e não só entre os dois
    /// comparados: com três empatados, o que vale é a mini-tabela entre eles, que é como
    /// a várzea resolve. Sem jogo entre eles, o critério não decide nada.
    /// </summary>
    private static int HeadToHead(
        TeamRecord left,
        TeamRecord right,
        IReadOnlyList<StandingsMatch> matches,
        IReadOnlyList<TeamRecord> all)
    {
        var empatados = all
            .Where(record => record.Points == left.Points)
            .Select(record => record.TeamId)
            .ToHashSet();
        var entreEles = matches
            .Where(match => empatados.Contains(match.HomeTeamId) && empatados.Contains(match.AwayTeamId))
            .ToList();
        if (entreEles.Count == 0)
        {
            return 0;
        }

        var mini = Build([left.TeamId, right.TeamId], entreEles);
        var deLeft = mini.Single(record => record.TeamId == left.TeamId);
        var deRight = mini.Single(record => record.TeamId == right.TeamId);
        if (deLeft.Points != deRight.Points)
        {
            return deRight.Points.CompareTo(deLeft.Points);
        }

        return deRight.GoalDifference.CompareTo(deLeft.GoalDifference);
    }
}
