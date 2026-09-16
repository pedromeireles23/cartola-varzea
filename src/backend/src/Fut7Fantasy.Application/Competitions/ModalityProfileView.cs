using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Application.Competitions;

/// <summary>Regras públicas de uma modalidade, como a interface e a página de regras mostram.</summary>
public sealed record ModalityProfileView(
    string Modality,
    int Version,
    int Starters,
    FormationView Formation,
    int BenchSize,
    int SquadAthletes,
    decimal Budget,
    int MinimumAthletesPerRealTeam,
    IReadOnlyList<RealTeamLimitView> RealTeamLimits,
    FallbackPricesView FallbackPrices)
{
    /// <summary>Faixas de times ativos que mudam o limite (01 §9).</summary>
    private static readonly int[] ActiveRealTeamBands = [4, 3, 2];

    public static ModalityProfileView From(ModalityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new ModalityProfileView(
            profile.Modality.ToString(),
            profile.Version,
            profile.Starters,
            new FormationView(
                profile.Formation.Goalkeepers,
                profile.Formation.Defenders,
                profile.Formation.Midfielders,
                profile.Formation.Forwards),
            profile.BenchSize,
            profile.SquadAthletes,
            profile.Budget,
            profile.MinimumAthletesPerRealTeam,
            [.. ActiveRealTeamBands.Select(activeRealTeams =>
            {
                var limit = profile.RealTeamLimitFor(activeRealTeams);
                return new RealTeamLimitView(activeRealTeams, limit.MaxStarters, limit.MaxAthletes);
            })],
            new FallbackPricesView(
                profile.FallbackPrices[Position.Goalkeeper],
                profile.FallbackPrices[Position.Defender],
                profile.FallbackPrices[Position.Midfielder],
                profile.FallbackPrices[Position.Forward],
                profile.CoachFallbackPrice));
    }
}

public sealed record FormationView(int Goalkeepers, int Defenders, int Midfielders, int Forwards);

/// <summary>Limite por time real numa faixa de times ativos; a faixa 4 vale para 4 ou mais.</summary>
public sealed record RealTeamLimitView(int ActiveRealTeams, int MaxStarters, int MaxAthletes);

public sealed record FallbackPricesView(
    decimal Goalkeeper,
    decimal Defender,
    decimal Midfielder,
    decimal Forward,
    decimal Coach);
