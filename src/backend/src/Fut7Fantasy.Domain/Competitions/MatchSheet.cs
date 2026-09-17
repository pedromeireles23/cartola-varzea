using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Competitions;

public sealed record MatchSheetError(string Field, string Message);

public enum RedCardReason
{
    SecondYellow = 1,
    Direct = 2,
}

public enum StatEventType
{
    Goal = 1,
    Assist = 2,
    GoalkeeperSave = 3,
    PenaltySave = 4,
    YellowCard = 5,
    RedCard = 6,
    OwnGoal = 7,
    PenaltyMiss = 8,
}

/// <summary>O que aconteceu com um atleta numa partida.</summary>
public sealed record MatchSheetAppearanceDefinition(
    Guid AthleteId,
    Guid RealTeamId,
    Position Position,
    bool DidPlay,
    bool PlayedAsGoalkeeper,
    int GoalsConceded,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    RedCardReason? RedCardReason,
    int OwnGoals,
    int PenaltyMisses)
{
    public bool HasStatistics => GoalsConceded != 0
        || Goals != 0
        || Assists != 0
        || GoalkeeperSaves != 0
        || PenaltySaves != 0
        || YellowCards != 0
        || RedCards != 0
        || OwnGoals != 0
        || PenaltyMisses != 0;

    public IEnumerable<StatEventDefinition> Events()
    {
        if (Goals > 0) yield return new(StatEventType.Goal, Goals, null);
        if (Assists > 0) yield return new(StatEventType.Assist, Assists, null);
        if (GoalkeeperSaves > 0) yield return new(StatEventType.GoalkeeperSave, GoalkeeperSaves, null);
        if (PenaltySaves > 0) yield return new(StatEventType.PenaltySave, PenaltySaves, null);
        if (YellowCards > 0) yield return new(StatEventType.YellowCard, YellowCards, null);
        if (RedCards > 0) yield return new(StatEventType.RedCard, RedCards, RedCardReason);
        if (OwnGoals > 0) yield return new(StatEventType.OwnGoal, OwnGoals, null);
        if (PenaltyMisses > 0) yield return new(StatEventType.PenaltyMiss, PenaltyMisses, null);
    }
}

public sealed record StatEventDefinition(
    StatEventType Type,
    int Quantity,
    RedCardReason? RedCardReason);

public sealed record MatchSheetDefinition(
    int HomeScore,
    int AwayScore,
    IReadOnlyList<MatchSheetAppearanceDefinition> Appearances)
{
    public IReadOnlyList<MatchSheetError> Validate(Guid homeTeamId, Guid awayTeamId)
    {
        var errors = new List<MatchSheetError>();
        Range(HomeScore, 0, 99, nameof(HomeScore), errors);
        Range(AwayScore, 0, 99, nameof(AwayScore), errors);
        if (Appearances.GroupBy(item => item.AthleteId).Any(group => group.Count() > 1))
        {
            errors.Add(new(nameof(Appearances), "Cada atleta aparece uma única vez na súmula."));
        }

        foreach (var appearance in Appearances)
        {
            ValidateAppearance(appearance, homeTeamId, awayTeamId, errors);
        }

        ValidateTeam(homeTeamId, HomeScore, AwayScore, errors);
        ValidateTeam(awayTeamId, AwayScore, HomeScore, errors);
        return errors;
    }

    private void ValidateTeam(Guid teamId, int score, int opponentScore, List<MatchSheetError> errors)
    {
        var teamAppearances = Appearances.Where(item => item.RealTeamId == teamId).ToArray();
        var goals = teamAppearances.Sum(item => item.Goals);
        var opponentOwnGoals = Appearances
            .Where(item => item.RealTeamId != teamId)
            .Sum(item => item.OwnGoals);
        if (goals + opponentOwnGoals != score)
        {
            errors.Add(new(nameof(Appearances), "Os gols dos atletas e gols contra precisam corresponder ao placar."));
        }

        var goalkeepers = teamAppearances.Where(item => item.DidPlay && item.PlayedAsGoalkeeper).ToArray();
        if (goalkeepers.Length == 0)
        {
            errors.Add(new(nameof(Appearances), "Cada time precisa ter ao menos um goleiro participante."));
        }
        else if (goalkeepers.Sum(item => item.GoalsConceded) != opponentScore)
        {
            errors.Add(new(
                nameof(Appearances),
                "Os gols sofridos pelos goleiros precisam corresponder ao placar adversário."));
        }
    }

    private static void ValidateAppearance(
        MatchSheetAppearanceDefinition item,
        Guid homeTeamId,
        Guid awayTeamId,
        List<MatchSheetError> errors)
    {
        if (item.RealTeamId != homeTeamId && item.RealTeamId != awayTeamId)
        {
            errors.Add(new(nameof(item.RealTeamId), "O atleta não pertence a um dos times da partida."));
        }

        if (!item.DidPlay && (item.PlayedAsGoalkeeper || item.HasStatistics))
        {
            errors.Add(new(
                nameof(item.DidPlay),
                "Atletas que não jogaram não podem ter posição em campo ou estatísticas."));
        }

        if (!item.PlayedAsGoalkeeper && item.GoalsConceded != 0)
        {
            errors.Add(new(nameof(item.GoalsConceded), "Somente goleiros podem registrar gols sofridos."));
        }

        Range(item.GoalsConceded, 0, 99, nameof(item.GoalsConceded), errors);
        Range(item.Goals, 0, 99, nameof(item.Goals), errors);
        Range(item.Assists, 0, 99, nameof(item.Assists), errors);
        Range(item.GoalkeeperSaves, 0, 999, nameof(item.GoalkeeperSaves), errors);
        Range(item.PenaltySaves, 0, 99, nameof(item.PenaltySaves), errors);
        Range(item.YellowCards, 0, 2, nameof(item.YellowCards), errors);
        Range(item.RedCards, 0, 1, nameof(item.RedCards), errors);
        Range(item.OwnGoals, 0, 99, nameof(item.OwnGoals), errors);
        Range(item.PenaltyMisses, 0, 99, nameof(item.PenaltyMisses), errors);

        if (item.RedCards == 1 && item.RedCardReason is null)
        {
            errors.Add(new(nameof(item.RedCardReason), "Informe se a expulsão foi direta ou por segundo amarelo."));
        }

        if (item.RedCards == 0 && item.RedCardReason is not null)
        {
            errors.Add(new(
                nameof(item.RedCardReason),
                "O motivo da expulsão só pode ser informado com cartão vermelho."));
        }

        if (item.RedCardReason == Fut7Fantasy.Domain.Competitions.RedCardReason.SecondYellow
            && item.YellowCards != 2)
        {
            errors.Add(new(nameof(item.YellowCards), "Expulsão por segundo amarelo exige dois cartões amarelos."));
        }
    }

    private static void Range(int value, int minimum, int maximum, string field, List<MatchSheetError> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(new(field, $"O valor deve estar entre {minimum} e {maximum}."));
        }
    }
}

public sealed class MatchSheet
{
    private MatchSheet()
    {
    }

    private MatchSheet(Guid id, Guid competitionId, Guid matchId, int homeScore, int awayScore, DateTimeOffset now)
    {
        Id = id;
        CompetitionId = competitionId;
        MatchId = matchId;
        HomeScore = homeScore;
        AwayScore = awayScore;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public Guid MatchId { get; private set; }
    public int HomeScore { get; private set; }
    public int AwayScore { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static MatchSheet Create(
        Guid id,
        Guid competitionId,
        Guid matchId,
        int homeScore,
        int awayScore,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || competitionId == Guid.Empty || matchId == Guid.Empty)
        {
            throw new ArgumentException("Súmula, campeonato e partida precisam ser identificados.");
        }

        return new(id, competitionId, matchId, homeScore, awayScore, now);
    }

    public void UpdateScore(int homeScore, int awayScore, DateTimeOffset now)
    {
        HomeScore = homeScore;
        AwayScore = awayScore;
        UpdatedAt = now;
    }
}

public sealed class AthleteAppearance
{
    private AthleteAppearance()
    {
    }

    private AthleteAppearance(
        Guid id,
        Guid competitionId,
        Guid matchSheetId,
        MatchSheetAppearanceDefinition definition)
    {
        Id = id;
        CompetitionId = competitionId;
        MatchSheetId = matchSheetId;
        AthleteId = definition.AthleteId;
        RealTeamId = definition.RealTeamId;
        Position = definition.Position;
        DidPlay = definition.DidPlay;
        PlayedAsGoalkeeper = definition.PlayedAsGoalkeeper;
        GoalsConceded = definition.GoalsConceded;
    }

    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public Guid MatchSheetId { get; private set; }
    public Guid AthleteId { get; private set; }
    public Guid RealTeamId { get; private set; }
    public Position Position { get; private set; }
    public bool DidPlay { get; private set; }
    public bool PlayedAsGoalkeeper { get; private set; }
    public int GoalsConceded { get; private set; }

    public static AthleteAppearance Create(
        Guid id,
        Guid competitionId,
        Guid matchSheetId,
        MatchSheetAppearanceDefinition definition)
    {
        if (id == Guid.Empty || competitionId == Guid.Empty || matchSheetId == Guid.Empty)
        {
            throw new ArgumentException("Participação, campeonato e súmula precisam ser identificados.");
        }

        return new(id, competitionId, matchSheetId, definition);
    }
}

public sealed class StatEvent
{
    private StatEvent()
    {
    }

    private StatEvent(
        Guid id,
        Guid competitionId,
        Guid matchSheetId,
        Guid athleteId,
        StatEventDefinition definition)
    {
        Id = id;
        CompetitionId = competitionId;
        MatchSheetId = matchSheetId;
        AthleteId = athleteId;
        Type = definition.Type;
        Quantity = definition.Quantity;
        RedCardReason = definition.RedCardReason;
    }

    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public Guid MatchSheetId { get; private set; }
    public Guid AthleteId { get; private set; }
    public StatEventType Type { get; private set; }
    public int Quantity { get; private set; }
    public RedCardReason? RedCardReason { get; private set; }

    public static StatEvent Create(
        Guid id,
        Guid competitionId,
        Guid matchSheetId,
        Guid athleteId,
        StatEventDefinition definition)
    {
        if (id == Guid.Empty || competitionId == Guid.Empty || matchSheetId == Guid.Empty || athleteId == Guid.Empty)
        {
            throw new ArgumentException("Evento, campeonato, súmula e atleta precisam ser identificados.");
        }

        return new(id, competitionId, matchSheetId, athleteId, definition);
    }
}
