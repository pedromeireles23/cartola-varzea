namespace Fut7Fantasy.Application.Scoring;

/// <summary>
/// O resultado das rodadas para quem joga (01 §9, 02 §8 "Score breakdown"). Lê sempre a
/// participação da conta da sessão, e só de rodada publicada: o que está em apuração não
/// vaza para o participante.
/// </summary>
public interface IFantasyScoreService
{
    /// <summary>
    /// Rodadas já apuradas, da mais recente para a mais antiga. Uma rodada reaberta para
    /// correção continua na lista com os números da apuração vigente: esconder o resultado
    /// até a republicação deixaria o participante sem referência nenhuma.
    /// </summary>
    Task<IReadOnlyList<FantasyRoundSummaryView>?> RoundsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>O detalhamento de uma rodada já apurada; nulo quando ela não existe ou não saiu.</summary>
    Task<FantasyRoundScoreView?> RoundAsync(string slug, Guid roundId, CancellationToken cancellationToken);
}

/// <summary>
/// Uma rodada apurada na lista. <see cref="Total"/> é nulo quando a conta não jogou a
/// rodada — entrou depois do fechamento ou estava com a escalação incompleta.
/// <see cref="UnderCorrection"/> diz que o organizador reabriu a rodada: os números
/// continuam sendo os da apuração vigente e podem mudar quando ele republicar.
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
    decimal? Total,
    bool UnderCorrection);

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
    IReadOnlyList<FantasyRoundSlotView> Slots,
    bool UnderCorrection,
    FantasyRoundCorrectionView? Correction);

/// <summary>
/// A correção que produziu a revisão vigente (01 §9): quando ela saiu, por que a rodada foi
/// reaberta e quanto a conta tinha antes. <see cref="PreviousTotal"/> é nulo quando a conta
/// não jogou a revisão anterior; <see cref="Reason"/> é nulo quando a rodada foi corrigida
/// enquanto ainda era provisória, quando o motivo não é exigido.
/// </summary>
public sealed record FantasyRoundCorrectionView(
    int Revision,
    string CorrectedAtLocal,
    string? Reason,
    decimal? PreviousTotal);

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
