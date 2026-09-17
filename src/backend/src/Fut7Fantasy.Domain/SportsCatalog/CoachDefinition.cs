namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>Dados editáveis do ativo de técnico de um time.</summary>
public sealed record CoachDefinition(
    string? DisplayName,
    PriceTier PriceTier,
    decimal? InitialPriceOverride)
{
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 80;

    public CoachDefinition Normalized()
    {
        var name = DisplayName?.Trim();
        return this with { DisplayName = string.IsNullOrEmpty(name) ? null : name };
    }

    public IReadOnlyList<SportsCatalogValidationError> Validate()
    {
        var normalized = Normalized();
        var errors = new List<SportsCatalogValidationError>();
        if (normalized.DisplayName is { Length: < DisplayNameMinLength or > DisplayNameMaxLength })
        {
            errors.Add(new(
                nameof(DisplayName),
                $"Quando informado, use de {DisplayNameMinLength} a {DisplayNameMaxLength} caracteres no nome."));
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
