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

        // Regra é catálogo do domínio, não dado de campeonato: quem quer entender como
        // se pontua não precisa antes escolher onde jogar.
        routes.MapGet("/api/v1/public/scoring-rules", () => Results.Ok(PublicScoringRules.Build()))
            .AllowAnonymous()
            .WithTags("Campeonatos públicos")
            .WithName("GetPublicScoringRules")
            .WithSummary("Pontuação e valorização vigentes das três modalidades.");

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

        competitions.MapGet("/{slug}/fixtures", FixturesAsync)
            .AllowAnonymous()
            .WithName("GetPublicCompetitionFixtures")
            .WithSummary("Calendário do campeonato; placar só nas rodadas com resultado publicado.");
        competitions.MapGet("/{slug}/standings", StandingsAsync)
            .AllowAnonymous()
            .WithName("GetPublicCompetitionStandings")
            .WithSummary("Classificação por fase e grupo; mata-mata vem sem tabela, de propósito.");
        competitions.MapGet("/{slug}/teams/{teamId:guid}", TeamAsync)
            .AllowAnonymous()
            .WithName("GetPublicTeam")
            .WithSummary("Time do campeonato, com o elenco inscrito e o técnico.");
        competitions.MapGet("/{slug}/athletes/{athleteId:guid}", AthleteAsync)
            .AllowAnonymous()
            .WithName("GetPublicAthlete")
            .WithSummary("Perfil público do atleta, com estatísticas e histórico de preço.");
        competitions.MapGet("/{slug}/matches/{matchId:guid}", MatchAsync)
            .AllowAnonymous()
            .WithName("GetPublicMatch")
            .WithSummary("Súmula de uma partida, disponível só depois que a rodada publica.");

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

    private static async Task<IResult> FixturesAsync(
        string slug,
        IPublicFixtureService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.FixturesAsync(slug, cancellationToken).ConfigureAwait(false) is { } fixtures
            ? Results.Ok(fixtures)
            : Results.NotFound();
    }

    /// <summary>
    /// Partida de rodada não publicada responde igual a partida inexistente: a diferença
    /// entre 404 e 403 contaria que o jogo já tem súmula lançada.
    /// </summary>
    private static async Task<IResult> MatchAsync(
        string slug,
        Guid matchId,
        IPublicFixtureService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.MatchAsync(slug, matchId, cancellationToken).ConfigureAwait(false) is { } match
            ? Results.Ok(match)
            : Results.NotFound();
    }

    private static async Task<IResult> StandingsAsync(
        string slug,
        IPublicFixtureService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.StandingsAsync(slug, cancellationToken).ConfigureAwait(false) is { } tabela
            ? Results.Ok(tabela)
            : Results.NotFound();
    }

    private static async Task<IResult> TeamAsync(
        string slug,
        Guid teamId,
        IPublicCatalogService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.TeamAsync(slug, teamId, cancellationToken).ConfigureAwait(false) is { } team
            ? Results.Ok(team)
            : Results.NotFound();
    }

    private static async Task<IResult> AthleteAsync(
        string slug,
        Guid athleteId,
        IPublicCatalogService service,
        CancellationToken cancellationToken)
    {
        if (!CompetitionSlug.IsValid(slug))
        {
            return Results.NotFound();
        }

        return await service.AthleteAsync(slug, athleteId, cancellationToken).ConfigureAwait(false) is { } athlete
            ? Results.Ok(athlete)
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
