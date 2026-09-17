namespace Fut7Fantasy.Domain.SportsCatalog;

/// <summary>Nível relativo usado para derivar o preço inicial de atletas e técnicos.</summary>
public enum PriceTier
{
    Basic = 1,
    Regular = 2,
    Star = 3,
}

/// <summary>Regras comuns de preço dos ativos do mercado (01 §9).</summary>
public static class SportsAssetPricing
{
    public const decimal MinimumPrice = 1m;
    public const decimal MaximumPrice = 30m;

    public static decimal Resolve(decimal fallbackPrice, PriceTier tier, decimal? exactPrice)
    {
        if (exactPrice is not null)
        {
            return exactPrice.Value;
        }

        var multiplier = tier switch
        {
            PriceTier.Star => 1.35m,
            PriceTier.Regular => 1m,
            PriceTier.Basic => 0.70m,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Nível de preço desconhecido."),
        };

        return Math.Round(fallbackPrice * multiplier * 2m, 0, MidpointRounding.AwayFromZero) / 2m;
    }

    public static bool IsValidExactPrice(decimal? exactPrice) =>
        exactPrice is null
        || exactPrice is >= MinimumPrice and <= MaximumPrice
        && decimal.Round(exactPrice.Value, 2) == exactPrice.Value;
}
