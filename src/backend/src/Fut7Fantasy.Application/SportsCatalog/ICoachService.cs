using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Application.SportsCatalog;

/// <summary>Leitura e edição dos técnicos criados automaticamente com os times.</summary>
public interface ICoachService
{
    Task<IReadOnlyList<CoachView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);

    Task<CoachCommandResult> UpdateAsync(
        Guid competitionId,
        Guid coachId,
        CoachDefinition definition,
        string version,
        CancellationToken cancellationToken);
}

public enum CoachCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,

    /// <summary>
    /// Nível e preço exato travam na primeira abertura de mercado em que o técnico esteve disponível.
    /// </summary>
    PriceLocked,
}

public sealed record CoachCommandResult(
    CoachCommandOutcome Outcome,
    CoachView? Coach,
    IReadOnlyList<SportsCatalogValidationError> Errors)
{
    public static CoachCommandResult Of(CoachCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record CoachView(
    Guid Id,
    string? DisplayName,
    string EffectiveName,
    Guid RealTeamId,
    string RealTeamName,
    string PriceTier,
    decimal? InitialPriceOverride,
    decimal InitialPrice,
    bool IsAvailable,
    bool IsEliminated,
    bool IsMarketLocked,
    DateTimeOffset UpdatedAt,
    string Version)
{
    public static CoachView From(Coach coach, RealTeam team, ModalityProfile profile, bool teamEliminated)
    {
        ArgumentNullException.ThrowIfNull(coach);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(profile);

        return new(
            coach.Id,
            coach.DisplayName,
            coach.EffectiveName(team.Name),
            coach.RealTeamId,
            team.Name,
            coach.PriceTier.ToString(),
            coach.InitialPriceOverride,
            profile.InitialCoachPrice(coach.PriceTier, coach.InitialPriceOverride),
            !team.IsArchived && !teamEliminated,
            teamEliminated,
            coach.FirstMarketAvailableAt is not null,
            coach.UpdatedAt,
            Convert.ToBase64String(coach.RowVersion));
    }
}
