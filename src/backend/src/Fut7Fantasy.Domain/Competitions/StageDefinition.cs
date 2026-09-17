namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Grupo pedido pela organização; sem <c>Id</c>, é um grupo novo.</summary>
public sealed record GroupDefinition(Guid? Id, string Name);

/// <summary>
/// O que a organização escolhe numa fase. As regras ficam aqui para que API e agregado
/// recusem os mesmos valores, como em <see cref="CompetitionSettings"/>.
/// </summary>
public sealed record StageDefinition(
    string Name,
    StageFormat Format,
    IReadOnlyList<GroupDefinition> Groups,
    IReadOnlyList<TiebreakCriterion> Tiebreakers)
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 60;
    public const int GroupNameMaxLength = 30;
    public const int MaxGroups = 16;

    /// <summary>
    /// Limite operacional do MVP. Não representa uma sequência esportiva obrigatória:
    /// nomes, formatos e ordem das fases continuam livres.
    /// </summary>
    public const int MaxStagesPerCompetition = 10;

    /// <summary>A ordem usada pela maioria dos regulamentos de campeonatos no Brasil.</summary>
    public static IReadOnlyList<TiebreakCriterion> DefaultTiebreakers { get; } =
    [
        TiebreakCriterion.Wins,
        TiebreakCriterion.GoalDifference,
        TiebreakCriterion.GoalsFor,
        TiebreakCriterion.HeadToHead,
        TiebreakCriterion.FewestRedCards,
        TiebreakCriterion.FewestYellowCards,
    ];

    public StageDefinition Normalized() => this with
    {
        Name = Name?.Trim() ?? string.Empty,
        Groups = [.. (Groups ?? []).Select(group => group with { Name = group.Name?.Trim() ?? string.Empty })],
        Tiebreakers = Tiebreakers ?? [],
    };

    public IReadOnlyList<CompetitionSettingsError> Validate()
    {
        var normalized = Normalized();
        var errors = new List<CompetitionSettingsError>();

        if (normalized.Name.Length is < NameMinLength or > NameMaxLength)
        {
            errors.Add(new(nameof(Name), $"Use de {NameMinLength} a {NameMaxLength} caracteres no nome da fase."));
        }

        if (!Enum.IsDefined(normalized.Format))
        {
            errors.Add(new(nameof(Format), "Escolha fase de grupos ou mata-mata."));
            return errors;
        }

        if (normalized.Format == StageFormat.Knockout)
        {
            if (normalized.Groups.Count > 0)
            {
                errors.Add(new(nameof(Groups), "Mata-mata não tem grupos."));
            }

            if (normalized.Tiebreakers.Count > 0)
            {
                errors.Add(new(nameof(Tiebreakers), "Mata-mata não tem classificação para desempatar."));
            }

            return errors;
        }

        if (normalized.Groups.Count is < 1 or > MaxGroups)
        {
            errors.Add(new(nameof(Groups), $"A fase de grupos tem de 1 a {MaxGroups} grupos."));
        }
        else if (normalized.Groups.Any(group => group.Name.Length is 0 or > GroupNameMaxLength))
        {
            errors.Add(new(nameof(Groups), $"Dê a cada grupo um nome com até {GroupNameMaxLength} caracteres."));
        }
        else if (normalized.Groups
            .GroupBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Any(same => same.Count() > 1))
        {
            errors.Add(new(nameof(Groups), "Os grupos precisam de nomes diferentes."));
        }
        else if (normalized.Groups
            .Where(group => group.Id is not null)
            .GroupBy(group => group.Id)
            .Any(same => same.Count() > 1))
        {
            errors.Add(new(nameof(Groups), "O mesmo grupo aparece duas vezes."));
        }

        if (normalized.Tiebreakers.Count == 0)
        {
            errors.Add(new(nameof(Tiebreakers), "Escolha ao menos um critério de desempate."));
        }
        else if (normalized.Tiebreakers.Any(criterion => !Enum.IsDefined(criterion))
            || normalized.Tiebreakers.Distinct().Count() != normalized.Tiebreakers.Count)
        {
            errors.Add(new(nameof(Tiebreakers), "Use cada critério de desempate uma única vez."));
        }

        return errors;
    }
}
