namespace Fut7Fantasy.Application.Scoring;

/// <summary>
/// O resultado das rodadas para quem joga (01 §9, 02 §8 "Score breakdown"). Lê sempre a
/// participação da conta da sessão, e só de rodada publicada: o que está em apuração não
/// vaza para o participante.
/// </summary>
public interface IFantasyScoreService
{
    /// <summary>Rodadas publicadas, da mais recente para a mais antiga.</summary>
    Task<IReadOnlyList<FantasyRoundSummaryView>?> RoundsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>O detalhamento de uma rodada publicada; nulo quando ela não existe ou não saiu.</summary>
    Task<FantasyRoundScoreView?> RoundAsync(string slug, Guid roundId, CancellationToken cancellationToken);
}

/// <summary>
/// Uma rodada apurada na lista. <see cref="Total"/> é nulo quando a conta não jogou a
/// rodada — entrou depois do fechamento ou estava com a escalação incompleta.
/// </summary>
public sealed record FantasyRoundSummaryView(
    Guid RoundId,
    string RoundName,
    int Sequence,
    DateTimeOffset PublishedAt,
    string PublishedAtLocal,
    string ConsolidatesAtLocal,
    bool Provisional,
    string TimeZoneId,
    decimal? Total);

/// <summary>
/// O detalhamento de uma rodada. <see cref="Played"/> falso quer dizer que a conta não
/// teve escalação congelada nessa rodada; nesse caso não há vagas para mostrar.
/// <see cref="Provisional"/> acompanha os números, e não só a tela da rodada (02 §8).
/// </summary>
public sealed record FantasyRoundScoreView(
    Guid RoundId,
    string RoundName,
    string PublishedAtLocal,
    string ConsolidatesAtLocal,
    bool Provisional,
    string TimeZoneId,
    int Revision,
    bool Played,
    decimal Total,
    decimal CaptainBonus,
    IReadOnlyList<FantasyRoundSlotView> Slots);

/// <summary>
/// Uma vaga da escalação congelada, com o que ela fez na rodada. <see cref="Counts"/> diz
/// se os pontos entraram no total: titular que jogou, reserva que entrou e técnico cujo
/// time teve alguém em campo.
/// </summary>
public sealed record FantasyRoundSlotView(
    string Kind,
    Guid AssetId,
    string Name,
    string? Position,
    string RealTeamName,
    string Role,
    bool Played,
    decimal Points,
    bool Counts,
    bool IsCaptain,
    Guid? Replaces,
    Guid? ReplacedBy,
    IReadOnlyList<FantasyScoreLineView> Lines,
    FantasyPriceChangeView? Price);

/// <summary>Um item que gerou pontos: "2 gols, +12,00".</summary>
public sealed record FantasyScoreLineView(string Item, int Quantity, decimal Points);

/// <summary>
/// A variação de preço explicada: "8,00 pts · 6,11 acima da média dos defensores · +1,5"
/// (01 §9). <see cref="Average"/> e <see cref="Difference"/> são nulos para quem não jogou.
/// </summary>
public sealed record FantasyPriceChangeView(
    decimal? Average,
    decimal? Difference,
    decimal Variation,
    decimal PreviousPrice,
    decimal NewPrice);
