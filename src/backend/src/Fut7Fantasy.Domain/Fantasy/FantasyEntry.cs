using System.Globalization;
using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Fantasy;

/// <summary>
/// Ativo como o mercado o oferece agora: preço atual, time e se pode ser comprado.
/// <see cref="Position"/> é nula para o técnico; <see cref="IsAvailable"/> exige
/// inscrição ativa, time não arquivado e não eliminado.
/// </summary>
public sealed record MarketAsset(
    AssetKind Kind,
    Guid Id,
    Position? Position,
    Guid RealTeamId,
    decimal Price,
    bool IsAvailable);

/// <summary>
/// As regras que valem para montar o elenco agora: a modalidade do campeonato e o
/// limite por time real, que depende de quantos times seguem ativos (01 §9).
/// </summary>
public sealed record SquadRules(ModalityProfile Profile, RealTeamLimit TeamLimit);

/// <summary>Por que uma operação no elenco foi recusada, com código estável para a interface.</summary>
public sealed record SquadRejection(string Code, string Message);

/// <summary>O que falta ou sobra para a escalação valer na rodada.</summary>
public sealed record LineupIssue(string Code, string Message);

/// <summary>
/// A participação de uma conta num campeonato: saldo, elenco e capitão (01 §9). Existe
/// no máximo uma por conta e campeonato.
///
/// O elenco é a própria escalação: cada posição tem as vagas de titular da formação e
/// uma de reserva, mais o técnico. O servidor recusa o que quebraria as regras na hora;
/// o que só falta completar aparece em <see cref="Issues"/>, porque montar o elenco
/// passa, necessariamente, por estados incompletos.
/// </summary>
public sealed class FantasyEntry
{
    /// <summary>Crédito escrito como o participante lê, independente da cultura do servidor.</summary>
    private static readonly CultureInfo Brazilian = CultureInfo.GetCultureInfo("pt-BR");

    private readonly List<SquadSlot> _slots = [];

    private FantasyEntry()
    {
    }

    private FantasyEntry(Guid id, Guid competitionId, Guid userId, decimal budget, DateTimeOffset joinedAt)
    {
        Id = id;
        CompetitionId = competitionId;
        UserId = userId;
        Balance = budget;
        JoinedAt = joinedAt;
        UpdatedAt = joinedAt;
    }

    public Guid Id { get; private set; }

    public Guid CompetitionId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Créditos livres. Começa no orçamento da modalidade e nunca fica negativo.</summary>
    public decimal Balance { get; private set; }

    /// <summary>Atleta titular que dobra os pontos. Não existe vice-capitão.</summary>
    public Guid? CaptainAthleteId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<SquadSlot> Slots => _slots;

    /// <summary>Adesão: o orçamento da modalidade é creditado aqui e só aqui.</summary>
    public static FantasyEntry Join(
        Guid id,
        Guid competitionId,
        Guid userId,
        ModalityProfile profile,
        DateTimeOffset joinedAt)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (id == Guid.Empty || competitionId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Participação, campeonato e conta precisam ser identificados.");
        }

        return new FantasyEntry(id, competitionId, userId, profile.Budget, joinedAt);
    }

    public bool Owns(AssetKind kind, Guid assetId) =>
        _slots.Any(slot => slot.Kind == kind && slot.AssetId == assetId);

    /// <summary>
    /// Compra e já escala: o atleta entra como titular se há vaga e o time dele não passou
    /// do limite de titulares; senão, vai para o banco.
    /// </summary>
    public SquadRejection? Buy(MarketAsset asset, SquadRules rules, DateTimeOffset now)
    {
        if (CanBuy(asset, rules) is { } rejection)
        {
            return rejection;
        }

        var role = asset.Kind == AssetKind.Coach
            ? SquadRole.Coach
            : PlacementFor(asset.Position!.Value, asset.RealTeamId, rules)!.Value;
        _slots.Add(new SquadSlot(Guid.CreateVersion7(), Id, asset, role, now));
        Balance -= asset.Price;
        UpdatedAt = now;
        return null;
    }

    /// <summary>
    /// Por que a compra seria recusada agora, sem mudar nada. É o que o mercado mostra em
    /// cada item bloqueado, e a mesma checagem que a compra faz.
    /// </summary>
    public SquadRejection? CanBuy(MarketAsset asset, SquadRules rules)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(rules);

        if (Owns(asset.Kind, asset.Id))
        {
            return new("already_owned", "Esse ativo já está no seu elenco.");
        }

        if (!asset.IsAvailable)
        {
            return new("unavailable", "Indisponível: desligado, arquivado ou de time eliminado.");
        }

        if (asset.Kind == AssetKind.Coach)
        {
            if (_slots.Any(slot => slot.Role == SquadRole.Coach))
            {
                return new("coach_taken", "Você já tem um técnico. Venda o atual para trocar.");
            }
        }
        else
        {
            var position = asset.Position
                ?? throw new ArgumentException("Atleta precisa de posição.", nameof(asset));
            if (AthletesOf(asset.RealTeamId) >= rules.TeamLimit.MaxAthletes)
            {
                return new(
                    "team_limit",
                    $"Limite do time: no máximo {rules.TeamLimit.MaxAthletes} atletas do mesmo time no elenco.");
            }

            if (PlacementFor(position, asset.RealTeamId, rules) is null)
            {
                return StartersOf(position) < rules.Profile.Formation.CountOf(position)
                    ? new(
                        "team_starter_limit",
                        $"Limite do time: no máximo {rules.TeamLimit.MaxStarters} titulares do mesmo time, "
                        + "e o banco dessa posição está ocupado.")
                    : new("position_full", "Posição completa: venda alguém dessa posição antes.");
            }
        }

        return Balance < asset.Price
            ? new("insufficient_balance", string.Create(Brazilian, $"Faltam C$ {asset.Price - Balance:0.00}."))
            : null;
    }

    /// <summary>Vende pelo preço atual. Vale também para ativo indisponível (01 §7).</summary>
    public SquadRejection? Sell(AssetKind kind, Guid assetId, decimal currentPrice, DateTimeOffset now)
    {
        var slot = _slots.SingleOrDefault(item => item.Kind == kind && item.AssetId == assetId);
        if (slot is null)
        {
            return new("not_owned", "Esse ativo não está no seu elenco.");
        }

        _slots.Remove(slot);
        Balance += currentPrice;
        if (CaptainAthleteId == assetId)
        {
            CaptainAthleteId = null;
        }

        UpdatedAt = now;
        return null;
    }

    /// <summary>Troca um titular com o reserva da mesma posição, como o banco faria.</summary>
    public SquadRejection? Swap(Guid starterAthleteId, Guid benchAthleteId, SquadRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var starter = AthleteSlot(starterAthleteId);
        var bench = AthleteSlot(benchAthleteId);
        if (starter?.Role != SquadRole.Starter || bench?.Role != SquadRole.Bench)
        {
            return new("invalid_swap", "Escolha um titular e um reserva do seu elenco.");
        }

        if (starter.Position != bench.Position)
        {
            return new("invalid_swap", "O reserva só entra no lugar de alguém da mesma posição.");
        }

        if (starter.RealTeamId != bench.RealTeamId
            && StartersFrom(bench.RealTeamId) >= rules.TeamLimit.MaxStarters)
        {
            return new(
                "team_starter_limit",
                $"Limite do time: no máximo {rules.TeamLimit.MaxStarters} titulares do mesmo time.");
        }

        starter.MoveTo(SquadRole.Bench);
        bench.MoveTo(SquadRole.Starter);
        if (CaptainAthleteId == starter.AssetId)
        {
            CaptainAthleteId = null;
        }

        UpdatedAt = now;
        return null;
    }

    public SquadRejection? SetCaptain(Guid athleteId, DateTimeOffset now)
    {
        if (AthleteSlot(athleteId)?.Role != SquadRole.Starter)
        {
            return new("invalid_captain", "O capitão precisa ser um titular do seu elenco.");
        }

        CaptainAthleteId = athleteId;
        UpdatedAt = now;
        return null;
    }

    /// <summary>
    /// O que impede a escalação de valer. Vazio quer dizer escalação completa: formação,
    /// banco, técnico, capitão e limites por time.
    /// </summary>
    public IReadOnlyList<LineupIssue> Issues(SquadRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        List<LineupIssue> issues = [];
        foreach (var position in Enum.GetValues<Position>())
        {
            var missingStarters = rules.Profile.Formation.CountOf(position) - StartersOf(position);
            if (missingStarters > 0)
            {
                issues.Add(new(
                    "missing_starter",
                    $"Falta{(missingStarters == 1 ? string.Empty : "m")} {missingStarters} "
                    + $"{PositionName(position, missingStarters)} entre os titulares."));
            }

            if (BenchOf(position) < ModalityProfile.BenchPerPosition)
            {
                issues.Add(new("missing_bench", $"Falta o reserva de {PositionName(position, 1)}."));
            }
        }

        if (!_slots.Any(slot => slot.Role == SquadRole.Coach))
        {
            issues.Add(new("missing_coach", "Falta o técnico."));
        }

        if (CaptainAthleteId is null)
        {
            issues.Add(new("missing_captain", "Escolha o capitão entre os titulares."));
        }

        var teams = _slots.Where(slot => slot.Kind == AssetKind.Athlete).GroupBy(slot => slot.RealTeamId);
        foreach (var team in teams)
        {
            if (team.Count() > rules.TeamLimit.MaxAthletes
                || team.Count(slot => slot.Role == SquadRole.Starter) > rules.TeamLimit.MaxStarters)
            {
                issues.Add(new(
                    "team_limit",
                    $"Um time passou do limite: {rules.TeamLimit.MaxStarters} titulares e "
                    + $"{rules.TeamLimit.MaxAthletes} atletas no elenco."));
            }
        }

        return issues;
    }

    private SquadRole? PlacementFor(Position position, Guid realTeamId, SquadRules rules)
    {
        if (StartersOf(position) < rules.Profile.Formation.CountOf(position)
            && StartersFrom(realTeamId) < rules.TeamLimit.MaxStarters)
        {
            return SquadRole.Starter;
        }

        return BenchOf(position) < ModalityProfile.BenchPerPosition ? SquadRole.Bench : null;
    }

    private SquadSlot? AthleteSlot(Guid athleteId) =>
        _slots.SingleOrDefault(slot => slot.Kind == AssetKind.Athlete && slot.AssetId == athleteId);

    private int StartersOf(Position position) =>
        _slots.Count(slot => slot.Role == SquadRole.Starter && slot.Position == position);

    private int BenchOf(Position position) =>
        _slots.Count(slot => slot.Role == SquadRole.Bench && slot.Position == position);

    private int StartersFrom(Guid realTeamId) =>
        _slots.Count(slot => slot.Role == SquadRole.Starter && slot.RealTeamId == realTeamId);

    private int AthletesOf(Guid realTeamId) =>
        _slots.Count(slot => slot.Kind == AssetKind.Athlete && slot.RealTeamId == realTeamId);

    private static string PositionName(Position position, int count) => (position, count) switch
    {
        (Position.Goalkeeper, 1) => "goleiro",
        (Position.Goalkeeper, _) => "goleiros",
        (Position.Defender, 1) => "defensor",
        (Position.Defender, _) => "defensores",
        (Position.Midfielder, 1) => "meio-campista",
        (Position.Midfielder, _) => "meio-campistas",
        (_, 1) => "atacante",
        _ => "atacantes",
    };
}
