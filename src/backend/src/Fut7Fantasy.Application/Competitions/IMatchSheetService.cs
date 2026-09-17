using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Competitions;

public interface IMatchSheetService
{
    Task<MatchSheetView?> GetAsync(
        Guid competitionId,
        Guid matchId,
        CancellationToken cancellationToken);

    Task<MatchSheetCommandResult> SaveAsync(
        Guid competitionId,
        Guid matchId,
        MatchSheetInput input,
        CancellationToken cancellationToken);
}

public enum MatchSheetCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,
    StatusLocked,
}

public sealed record MatchSheetCommandResult(
    MatchSheetCommandOutcome Outcome,
    MatchSheetView? Sheet,
    IReadOnlyList<MatchSheetError> Errors)
{
    public static MatchSheetCommandResult Of(MatchSheetCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record MatchSheetInput(
    int HomeScore,
    int AwayScore,
    IReadOnlyList<MatchSheetAppearanceInput> Appearances,
    string? Version);

public sealed record MatchSheetAppearanceInput(
    Guid AthleteId,
    bool DidPlay,
    bool PlayedAsGoalkeeper,
    int GoalsConceded,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    string? RedCardReason,
    int OwnGoals,
    int PenaltyMisses);

public sealed record MatchSheetView(
    Guid MatchId,
    Guid RoundId,
    string RoundName,
    string RoundPhase,
    DateTimeOffset KickoffAt,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid AwayTeamId,
    string AwayTeamName,
    int HomeScore,
    int AwayScore,
    IReadOnlyList<MatchSheetAthleteView> Athletes,
    string? Version);

public sealed record MatchSheetAthleteView(
    Guid AthleteId,
    Guid RealTeamId,
    string SportingName,
    string Position,
    bool DidPlay,
    bool PlayedAsGoalkeeper,
    int GoalsConceded,
    int Goals,
    int Assists,
    int GoalkeeperSaves,
    int PenaltySaves,
    int YellowCards,
    int RedCards,
    string? RedCardReason,
    int OwnGoals,
    int PenaltyMisses);
