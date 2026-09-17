namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>Dados editáveis do atleta e da inscrição dele no campeonato.</summary>
public sealed record AthleteDefinition(
    string SportingName,
    Position Position,
    PriceTier PriceTier,
    decimal? InitialPriceOverride)
{
    public const int SportingNameMinLength = 2;
    public const int SportingNameMaxLength = 80;

    public AthleteDefinition Normalized() => this with
    {
        SportingName = SportingName?.Trim() ?? string.Empty,
    };

    public IReadOnlyList<SportsCatalogValidationError> Validate()
    {
        var normalized = Normalized();
        var errors = new List<SportsCatalogValidationError>();
        if (normalized.SportingName.Length is < SportingNameMinLength or > SportingNameMaxLength)
        {
            errors.Add(new(
                nameof(SportingName),
                $"Use de {SportingNameMinLength} a {SportingNameMaxLength} caracteres no nome esportivo."));
        }

        if (!Enum.IsDefined(normalized.Position))
        {
            errors.Add(new(nameof(Position), "Escolha uma posição válida."));
        }

        if (!Enum.IsDefined(normalized.PriceTier))
        {
            errors.Add(new(nameof(PriceTier), "Escolha um nível de preço válido."));
        }

        if (!SportsAssetPricing.IsValidExactPrice(normalized.InitialPriceOverride))
        {
            errors.Add(new(
                nameof(InitialPriceOverride),
                $"Use um preço entre {SportsAssetPricing.MinimumPrice:0.00} e "
                + $"{SportsAssetPricing.MaximumPrice:0.00}, com até duas casas decimais."));
        }

        return errors;
    }
}
