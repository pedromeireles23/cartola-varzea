using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Application.SportsCatalog;

/// <summary>Cadastro manual de atletas e das inscrições deles nos times do campeonato.</summary>
public interface IAthleteService
{
    Task<IReadOnlyList<AthleteView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);

    Task<AthleteCommandResult> CreateAsync(
        Guid competitionId,
        Guid realTeamId,
        AthleteDefinition definition,
        CancellationToken cancellationToken);

    Task<AthleteCommandResult> UpdateAsync(
        Guid competitionId,
        Guid athleteId,
        Guid realTeamId,
        AthleteDefinition definition,
        string version,
        CancellationToken cancellationToken);

    Task<AthleteCommandOutcome> ReleaseAsync(
        Guid competitionId,
        Guid athleteId,
        CancellationToken cancellationToken);
}

public enum AthleteCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,
    Duplicate,
    TeamUnavailable,
    TransferNotAllowed,
    PositionLocked,
}

public sealed record AthleteCommandResult(
    AthleteCommandOutcome Outcome,
    AthleteView? Athlete,
    IReadOnlyList<SportsCatalogValidationError> Errors)
{
    public static AthleteCommandResult Of(AthleteCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record AthleteView(
    Guid Id,
    string SportingName,
    string Position,
    Guid RealTeamId,
    string RealTeamName,
    string PriceTier,
    decimal? InitialPriceOverride,
    decimal InitialPrice,
    bool IsAvailable,
    bool IsEliminated,
    string Status,
    DateTimeOffset UpdatedAt,
    string Version)
{
    public static AthleteView From(
        Athlete athlete,
        RosterRegistration registration,
        RealTeam team,
        ModalityProfile profile,
        bool teamEliminated)
    {
        ArgumentNullException.ThrowIfNull(athlete);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(profile);

        return new(
            athlete.Id,
            athlete.SportingName,
            athlete.Position.ToString(),
            registration.RealTeamId,
            team.Name,
            registration.PriceTier.ToString(),
            registration.InitialPriceOverride,
            profile.InitialAthletePrice(
                athlete.Position,
                registration.PriceTier,
                registration.InitialPriceOverride),
            registration.IsActive && !team.IsArchived && !teamEliminated,
            teamEliminated,
            registration.Status.ToString(),
            athlete.UpdatedAt,
            Convert.ToBase64String(athlete.RowVersion));
    }
}
