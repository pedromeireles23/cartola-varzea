using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Application.SportsCatalog;

/// <summary>Cadastro manual dos times reais de um campeonato.</summary>
public interface IRealTeamService
{
    Task<IReadOnlyList<RealTeamView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);

    Task<RealTeamCommandResult> CreateAsync(
        Guid competitionId,
        RealTeamDefinition definition,
        CancellationToken cancellationToken);

    Task<RealTeamCommandResult> UpdateAsync(
        Guid competitionId,
        Guid teamId,
        RealTeamDefinition definition,
        string version,
        CancellationToken cancellationToken);

    Task<RealTeamCommandOutcome> ArchiveAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken);
}

public enum RealTeamCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,
    Duplicate,
}

public sealed record RealTeamCommandResult(
    RealTeamCommandOutcome Outcome,
    RealTeamView? Team,
    IReadOnlyList<SportsCatalogValidationError> Errors)
{
    public static RealTeamCommandResult Of(RealTeamCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record RealTeamView(
    Guid Id,
    string Name,
    bool IsArchived,
    DateTimeOffset UpdatedAt,
    string Version)
{
    public static RealTeamView From(RealTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return new(
            team.Id,
            team.Name,
            team.IsArchived,
            team.UpdatedAt,
            Convert.ToBase64String(team.RowVersion));
    }
}
