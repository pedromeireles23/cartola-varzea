namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Estado da rodada que alguém decidiu e o banco guarda.
///
/// Os estados do ciclo documentado que o relógio alcança sozinho — `MarketClosed`,
/// `InProgress` e `Consolidated` — não estão aqui de propósito: guardá-los exigiria um
/// job para virar a chave na hora certa, e a rodada ficaria errada se o job falhasse.
/// Eles são derivados em <see cref="RoundPhase"/> (01 §8, 03 §7).
/// </summary>
public enum RoundStatus
{
    /// <summary>Em montagem: partidas podem ser criadas, alteradas e removidas.</summary>
    Draft = 1,

    /// <summary>Mercado aberto. Fecha sozinho quando o relógio passa de `MarketCloseAt`.</summary>
    MarketOpen = 2,

    /// <summary>
    /// Partidas encerradas, súmulas em conferência. Uma rodada que já foi publicada e
    /// voltou para cá está em correção; quem separa os dois casos é `PublishedAt`.
    /// </summary>
    UnderReview = 3,

    /// <summary>Resultado publicado; nasce provisório e consolida pelo relógio.</summary>
    Published = 4,

    /// <summary>Rodada que não aconteceu. Não volta atrás.</summary>
    Cancelled = 5,
}

/// <summary>
/// O ciclo de vida completo da rodada, como o participante o enxerga (01 §8). Junta o
/// que foi decidido com o que o relógio já alcançou.
/// </summary>
public enum RoundPhase
{
    Draft = 1,
    MarketOpen = 2,
    MarketClosed = 3,
    InProgress = 4,
    UnderReview = 5,
    Published = 6,
    Consolidated = 7,
    Cancelled = 8,

    /// <summary>
    /// Rodada publicada que voltou para conferência (01 §8, estado excepcional). A
    /// apuração vigente continua valendo até a republicação trocar tudo de uma vez.
    /// </summary>
    ReopenedForCorrection = 9,
}

/// <summary>O que cada fase da rodada permite, para a regra não ser reescrita em cada caso de uso.</summary>
public static class RoundPhases
{
    /// <summary>
    /// Súmula aceita escrita enquanto a rodada está em andamento, em conferência ou
    /// reaberta para correção — a reabertura existe exatamente para corrigir a súmula.
    /// </summary>
    public static bool AcceptsSheetChanges(RoundPhase phase) =>
        phase is RoundPhase.InProgress or RoundPhase.UnderReview or RoundPhase.ReopenedForCorrection;
}

/// <summary>Transições válidas entre os estados guardados.</summary>
public static class RoundLifecycle
{
    private static readonly Dictionary<RoundStatus, RoundStatus[]> Allowed = new()
    {
        [RoundStatus.Draft] = [RoundStatus.MarketOpen, RoundStatus.Cancelled],

        // Voltar para rascunho só vale enquanto o mercado não fechou; quem cuida
        // disso é o agregado, que conhece o relógio.
        [RoundStatus.MarketOpen] = [RoundStatus.Draft, RoundStatus.UnderReview, RoundStatus.Cancelled],
        [RoundStatus.UnderReview] = [RoundStatus.Published],

        // Reabrir para correção devolve a rodada à conferência; republicar passa pelo
        // mesmo caminho da primeira publicação.
        [RoundStatus.Published] = [RoundStatus.UnderReview],
        [RoundStatus.Cancelled] = [],
    };

    public static bool Allows(RoundStatus from, RoundStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<RoundStatus> From(RoundStatus status) =>
        Allowed.TryGetValue(status, out var targets) ? targets : [];
}
