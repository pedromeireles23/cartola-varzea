using Fut7Fantasy.Application.Competitions;

namespace Fut7Fantasy.Demo;

/// <summary>
/// Monta uma súmula verossímil a partir do placar: um goleiro em campo por time e o outro
/// no banco, um ou outro jogador de linha ausente — para o banco do fantasy ter quem
/// cobrir —, gols mais para atacante que para zagueiro, assistência na maioria dos gols,
/// defesas e um ou outro amarelo. Tudo pelo <see cref="Random"/> semeado: o mesmo reset
/// escreve sempre a mesma súmula.
/// </summary>
internal static class SheetFactory
{
    private static readonly Dictionary<string, int> GoalWeight = new(StringComparer.Ordinal)
    {
        ["Forward"] = 6,
        ["Midfielder"] = 3,
        ["Defender"] = 1,
    };

    public static MatchSheetInput Build(MatchSheetView sheet, int homeScore, int awayScore, Random random)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(random);

        var lines = new List<Line>();
        lines.AddRange(TeamLines(sheet, sheet.HomeTeamId, homeScore, awayScore, random));
        lines.AddRange(TeamLines(sheet, sheet.AwayTeamId, awayScore, homeScore, random));

        return new MatchSheetInput(
            homeScore,
            awayScore,
            [.. lines.Select(line => new MatchSheetAppearanceInput(
                line.AthleteId,
                line.DidPlay,
                line.PlayedAsGoalkeeper,
                line.GoalsConceded,
                line.Goals,
                line.Assists,
                line.GoalkeeperSaves,
                line.PenaltySaves,
                line.YellowCards,
                RedCards: 0,
                RedCardReason: null,
                OwnGoals: 0,
                PenaltyMisses: 0))],
            sheet.Version);
    }

    private static List<Line> TeamLines(
        MatchSheetView sheet,
        Guid teamId,
        int scored,
        int conceded,
        Random random)
    {
        // Pelo nome: a ordem do banco segue os identificadores, que mudam a cada reset.
        var squad = sheet.Athletes
            .Where(athlete => athlete.RealTeamId == teamId)
            .OrderBy(athlete => athlete.SportingName, StringComparer.Ordinal)
            .ToList();
        var lines = squad.Select(athlete => new Line(athlete.AthleteId, athlete.Position)).ToList();

        // Um goleiro em campo, o outro no banco; ele leva o placar do adversário.
        var keepers = lines.Where(line => line.Position == "Goalkeeper").ToList();
        var starter = keepers[random.Next(keepers.Count)];
        foreach (var keeper in keepers)
        {
            keeper.DidPlay = keeper == starter;
        }

        starter.PlayedAsGoalkeeper = true;
        starter.GoalsConceded = conceded;
        starter.GoalkeeperSaves = random.Next(1, 7);
        starter.PenaltySaves = random.Next(10) == 0 ? 1 : 0;

        // Às vezes alguém de linha falta: é o que faz o reserva do fantasy entrar.
        var outfield = lines.Where(line => line.Position != "Goalkeeper").ToList();
        if (outfield.Count > 6 && random.Next(3) == 0)
        {
            outfield[random.Next(outfield.Count)].DidPlay = false;
        }

        var playing = outfield.Where(line => line.DidPlay).ToList();
        for (var goal = 0; goal < scored; goal++)
        {
            var scorer = Weighted(playing, random);
            scorer.Goals++;
            if (random.Next(10) < 6)
            {
                var others = playing.Where(line => line != scorer).ToList();
                others[random.Next(others.Count)].Assists++;
            }
        }

        var booked = random.Next(3);
        for (var card = 0; card < booked; card++)
        {
            playing[random.Next(playing.Count)].YellowCards = 1;
        }

        return lines;
    }

    private static Line Weighted(List<Line> candidates, Random random)
    {
        var total = candidates.Sum(line => GoalWeight.GetValueOrDefault(line.Position, 1));
        var pick = random.Next(total);
        foreach (var line in candidates)
        {
            pick -= GoalWeight.GetValueOrDefault(line.Position, 1);
            if (pick < 0)
            {
                return line;
            }
        }

        return candidates[^1];
    }

    private sealed class Line(Guid athleteId, string position)
    {
        public Guid AthleteId { get; } = athleteId;

        public string Position { get; } = position;

        public bool DidPlay { get; set; } = true;

        public bool PlayedAsGoalkeeper { get; set; }

        public int GoalsConceded { get; set; }

        public int Goals { get; set; }

        public int Assists { get; set; }

        public int GoalkeeperSaves { get; set; }

        public int PenaltySaves { get; set; }

        public int YellowCards { get; set; }
    }
}
