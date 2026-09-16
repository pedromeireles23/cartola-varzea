using Fut7Fantasy.Domain.Competitions;
using Fut7Fantasy.Domain.SportsCatalog;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>Tradução entre o corpo das requisições e as regras do domínio.</summary>
internal static class DomainRequests
{
    /// <summary>
    /// Aceita só o nome do valor. Números passariam no <see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/>
    /// e criariam uma dependência do valor interno da enumeração.
    /// </summary>
    public static bool TryParseName<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        return value is not null
            && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, out parsed);
    }

    /// <summary>Problem Details de validação agrupado por campo.</summary>
    public static IResult ValidationProblem(
        IEnumerable<CompetitionSettingsError> errors,
        Func<string, string>? fieldName = null) =>
        ValidationProblem(errors, error => error.Field, error => error.Message, fieldName);

    /// <summary>Mesmo contrato de validação para o módulo de catálogo esportivo.</summary>
    public static IResult SportsCatalogValidationProblem(
        IEnumerable<SportsCatalogValidationError> errors,
        Func<string, string>? fieldName = null) =>
        ValidationProblem(errors, error => error.Field, error => error.Message, fieldName);

    private static IResult ValidationProblem<TError>(
        IEnumerable<TError> errors,
        Func<TError, string> field,
        Func<TError, string> message,
        Func<string, string>? fieldName) =>
        Results.ValidationProblem(
            errors
                .GroupBy(error => fieldName?.Invoke(field(error)) ?? field(error), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(message).ToArray(),
                    StringComparer.Ordinal),
            title: "Dados inválidos");
}
