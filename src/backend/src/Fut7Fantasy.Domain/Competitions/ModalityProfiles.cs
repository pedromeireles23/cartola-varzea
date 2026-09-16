using System.Collections.Frozen;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Catálogo versionado de perfis de modalidade. Os valores vêm da calibração de
/// 2026-09-16 (01 §7 e §9, simulações `modalidades-v1`); mudá-los exige uma versão nova,
/// nunca editar a existente.
/// </summary>
public static class ModalityProfiles
{
    /// <summary>
    /// Fallback igual nas três modalidades: o que muda entre elas é o orçamento, derivado
    /// do tamanho do elenco.
    /// </summary>
    private static readonly FrozenDictionary<Position, decimal> FallbackPricesV1 =
        new Dictionary<Position, decimal>
        {
            [Position.Goalkeeper] = 7m,
            [Position.Defender] = 7m,
            [Position.Midfielder] = 8m,
            [Position.Forward] = 9m,
        }.ToFrozenDictionary();

    private const decimal CoachFallbackPriceV1 = 8m;

    private static readonly ModalityProfile[] Versions =
    [
        new(Modality.Fut7, 1, new Formation(1, 2, 2, 2), 100m, FallbackPricesV1, CoachFallbackPriceV1),
        new(Modality.Futsal, 1, new Formation(1, 1, 2, 1), 80m, FallbackPricesV1, CoachFallbackPriceV1),
        new(Modality.Field, 1, new Formation(1, 4, 3, 3), 135m, FallbackPricesV1, CoachFallbackPriceV1),
    ];

    /// <summary>Versão vigente de cada modalidade, na ordem da enumeração.</summary>
    public static IReadOnlyList<ModalityProfile> Current { get; } =
        [.. Enum.GetValues<Modality>().Select(CurrentFor)];

    /// <summary>Versão usada por campeonatos novos.</summary>
    public static ModalityProfile CurrentFor(Modality modality) =>
        Versions.Where(profile => profile.Modality == modality).MaxBy(profile => profile.Version)
        ?? throw new ArgumentOutOfRangeException(nameof(modality), modality, "Modalidade sem perfil.");

    /// <summary>Uma versão específica, como a que um campeonato guardou ao ser criado.</summary>
    public static ModalityProfile? Find(Modality modality, int version) =>
        Versions.SingleOrDefault(profile => profile.Modality == modality && profile.Version == version);
}
