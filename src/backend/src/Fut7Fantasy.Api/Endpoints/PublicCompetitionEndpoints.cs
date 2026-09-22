using Fut7Fantasy.Application.Competitions;
using Fut7Fantasy.Application.Scoring;
using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Api.Endpoints;

/// <summary>
/// Leitura pública de campeonatos, sem conta e sem cookie. As rotas ficam separadas das
/// da organização de propósito: aqui nada é filtrado por papel, então a única coisa que
/// pode aparecer é o que já foi publicado.
/// </summary>
public static class PublicCompetitionEndpoints
{
    public static IEndpointRouteBuilder MapPublicCompetitionEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var competitions = routes.MapGroup("/api/v1/public/competitions")
            .WithTags("Campeonatos públicos");

        competitions.MapGet("/", SearchAsync)
            .AllowAnonymous()
            .WithName("SearchPublicCompetitions")
            .WithSummary("Campeonatos publicados, filtrados por nome, temporada ou organização.");
        competitions.MapGet("/{slug}", GetAsync)
            .AllowAnonymous()
            .WithName("GetPublicCompetition")
            .WithSummary("Campeonato publicado pelo endereço público, sem campos administrativos.");

        competitions.MapGet("/{slug}/ranking", RankingAsync)
            .AllowAnonymous()
            .WithName("GetPublicCompetitionRanking")
            .WithSummary("Ranking geral acumulado do campeonato publicado, sem conta.");

        return routes;
    }

    private static async Task<IResult> RankingAsync(
        string slug,
        IRankingService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.GeneralAsync(slug, cancellationToken).ConfigureAwait(false) is { } ranking
            ? Results.Ok(ranking)
            : Results.NotFound();
    }

    private static async Task<IResult> SearchAsync(
        string? busca,
        IPublicCompetitionService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.SearchAsync(busca, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(
        string slug,
        IPublicCompetitionService service,
        CancellationToken cancellationToken)
    {
        // Um slug fora do formato não existe; recusar antes evita consulta inútil.
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.GetAsync(slug, cancellationToken).ConfigureAwait(false) is { } competition
            ? Results.Ok(competition)
            : Results.NotFound();
    }
}
