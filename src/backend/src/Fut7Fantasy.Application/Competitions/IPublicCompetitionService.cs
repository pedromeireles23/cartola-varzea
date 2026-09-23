namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Leitura pública de campeonatos, sem conta. Só enxerga o que está publicado: um
/// rascunho responde igual a um campeonato inexistente, para que a existência de um
/// rascunho não vaze pela diferença entre 404 e 403.
/// </summary>
public interface IPublicCompetitionService
{
    /// <summary>
    /// Campeonatos publicados, filtrados por texto livre em nome, temporada e organização.
    /// Busca vazia devolve os mais recentes.
    /// </summary>
    Task<IReadOnlyList<PublicCompetitionSummary>> SearchAsync(
        string? query,
        CancellationToken cancellationToken);

    Task<PublicCompetitionView?> GetAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>
/// Linha da busca pública. Sem GUID: o endereço do campeonato para quem está de fora é
/// o slug.
/// </summary>
public sealed record PublicCompetitionSummary(
    string Slug,
    string Name,
    string Season,
    string Modality,
    string OrganizationName,
    DateTimeOffset PublishedAt);

/// <summary>
/// Time de um campeonato, na visão pública. O identificador entra para que a página do
/// campeonato consiga levar ao elenco; ele não é adivinhável e não diz nada sozinho.
/// </summary>
public sealed record PublicTeamView(Guid Id, string Name, int Athletes);

/// <summary>Grupo de uma fase com os times que a organização confirmou nele.</summary>
public sealed record PublicStageGroupView(string Name, IReadOnlyList<string> Teams);

/// <summary>
/// Fase na visão pública. No mata-mata não há grupos, e os times confirmados vêm em
/// <see cref="Teams"/>.
/// </summary>
public sealed record PublicStageView(
    string Name,
    string Format,
    int Sequence,
    IReadOnlyList<PublicStageGroupView> Groups,
    IReadOnlyList<string> Teams);

/// <summary>
/// Campeonato publicado como o visitante vê.
///
/// Guarda só o que é do jogo: nada de identificadores internos, versão de concorrência,
/// prazos administrativos de apuração ou quem criou o campeonato.
/// </summary>
public sealed record PublicCompetitionView(
    string Slug,
    string Name,
    string Season,
    string Modality,
    string OrganizationName,
    string TimeZoneId,
    DateTimeOffset PublishedAt,
    ModalityProfileView ModalityProfile,
    IReadOnlyList<PublicStageView> Stages,
    IReadOnlyList<PublicTeamView> Teams);
