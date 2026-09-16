namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>Valor inválido do catálogo esportivo, associado ao campo de entrada.</summary>
public sealed record SportsCatalogValidationError(string Field, string Message);

/// <summary>Dados editáveis de um time real inscrito no campeonato.</summary>
public sealed record RealTeamDefinition(string Name)
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;

    public RealTeamDefinition Normalized() => this with { Name = Name?.Trim() ?? string.Empty };

    public IReadOnlyList<SportsCatalogValidationError> Validate()
    {
        var normalized = Normalized();
        return normalized.Name.Length is < NameMinLength or > NameMaxLength
            ? [new(nameof(Name), $"Use de {NameMinLength} a {NameMaxLength} caracteres no nome do time.")]
            : [];
    }
}
