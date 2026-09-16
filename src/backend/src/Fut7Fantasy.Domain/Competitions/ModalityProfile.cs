using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Quantidade de titulares por posição.</summary>
public sealed record Formation(int Goalkeepers, int Defenders, int Midfielders, int Forwards)
{
    public int Starters => Goalkeepers + Defenders + Midfielders + Forwards;

    public int CountOf(Position position) => position switch
    {
        Position.Goalkeeper => Goalkeepers,
        Position.Defender => Defenders,
        Position.Midfielder => Midfielders,
        Position.Forward => Forwards,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Posição desconhecida."),
    };
}

/// <summary>Máximo de atletas de um mesmo time real numa equipe fantasy.</summary>
/// <param name="MaxStarters">Máximo entre os titulares.</param>
/// <param name="MaxAthletes">Máximo no elenco de atletas, titulares e banco. O técnico não conta.</param>
public sealed record RealTeamLimit(int MaxStarters, int MaxAthletes);

/// <summary>
/// Parâmetros que a modalidade impõe ao campeonato: formação, banco, orçamento, limite
/// por time real e preço de fallback (01 §7 e §9).
///
/// É versionado porque o campeonato guarda a versão com que foi criado: recalibrar uma
/// modalidade gera uma versão nova e não muda campeonato em andamento por acidente.
/// </summary>
public sealed class ModalityProfile
{
    /// <summary>O banco tem um reserva por posição, em todas as modalidades.</summary>
    public const int BenchPerPosition = 1;

    /// <summary>Folga mínima de atletas inscritos por time real além dos titulares (01 §7).</summary>
    public const int RealTeamRosterSlack = 2;

    internal ModalityProfile(
        Modality modality,
        int version,
        Formation formation,
        decimal budget,
        IReadOnlyDictionary<Position, decimal> fallbackPrices,
        decimal coachFallbackPrice)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A versão começa em 1.");
        }

        if (Enum.GetValues<Position>().Any(position => formation.CountOf(position) < 1))
        {
            throw new ArgumentException("A formação precisa de ao menos um titular por posição.", nameof(formation));
        }

        if (Enum.GetValues<Position>().Any(position => !fallbackPrices.ContainsKey(position)))
        {
            throw new ArgumentException("Toda posição precisa de preço de fallback.", nameof(fallbackPrices));
        }

        Modality = modality;
        Version = version;
        Formation = formation;
        BenchSize = Enum.GetValues<Position>().Length * BenchPerPosition;
        Budget = budget;
        FallbackPrices = fallbackPrices;
        CoachFallbackPrice = coachFallbackPrice;
    }

    public Modality Modality { get; }

    public int Version { get; }

    public Formation Formation { get; }

    public int Starters => Formation.Starters;

    public int BenchSize { get; }

    /// <summary>Atletas no elenco fantasy, sem contar o técnico.</summary>
    public int SquadAthletes => Starters + BenchSize;

    /// <summary>Orçamento inicial, em créditos virtuais.</summary>
    public decimal Budget { get; }

    public IReadOnlyDictionary<Position, decimal> FallbackPrices { get; }

    public decimal CoachFallbackPrice { get; }

    /// <summary>
    /// Abaixo disso, o checklist de publicação alerta que o time real tem poucos atletas
    /// inscritos para a modalidade.
    /// </summary>
    public int MinimumAthletesPerRealTeam => Starters + RealTeamRosterSlack;

    /// <summary>
    /// Limite por time real conforme quantos times seguem ativos na rodada (01 §9). O
    /// limite afrouxa perto da final porque um limite fixo tornaria a escalação impossível.
    /// </summary>
    public RealTeamLimit RealTeamLimitFor(int activeRealTeams)
    {
        // Com 4 ou mais times, os titulares vêm de pelo menos três times reais.
        var maxStarters = activeRealTeams switch
        {
            >= 4 => CeilingDivision(Starters, 3),
            3 => CeilingDivision(Starters, 2),

            // ⌈titulares ÷ 1,5⌉ em aritmética inteira.
            2 => CeilingDivision(Starters * 2, 3),
            _ => throw new ArgumentOutOfRangeException(
                nameof(activeRealTeams), activeRealTeams, "Uma rodada precisa de ao menos dois times ativos."),
        };
        var squadSlack = activeRealTeams == 2 ? 3 : 2;

        return new RealTeamLimit(maxStarters, maxStarters + squadSlack);
    }

    private static int CeilingDivision(int dividend, int divisor) => (dividend + divisor - 1) / divisor;
}
