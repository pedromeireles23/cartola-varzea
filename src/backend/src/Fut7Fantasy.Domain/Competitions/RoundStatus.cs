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

    /// <summary>Partidas encerradas, súmulas em revisão antes de publicar.</summary>
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

        // Republicar depois de uma correção passa de novo pela revisão (Fase 10).
        [RoundStatus.Published] = [RoundStatus.UnderReview],
        [RoundStatus.Cancelled] = [],
    };

    public static bool Allows(RoundStatus from, RoundStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<RoundStatus> From(RoundStatus status) =>
        Allowed.TryGetValue(status, out var targets) ? targets : [];
}
