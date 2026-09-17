using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Checklist de prontidão e publicação do campeonato. A policy de campeonato já validou
/// o escopo: a equipe inteira lê o checklist, só o proprietário publica.
/// </summary>
public interface ICompetitionPublicationService
{
    Task<CompetitionReadinessView?> GetReadinessAsync(Guid competitionId, CancellationToken cancellationToken);

    /// <summary>
    /// Publica ou despublica. <paramref name="version"/> é a versão lida pela tela, para
    /// que a decisão valha sobre o campeonato que a pessoa viu. Pedir o estado atual não
    /// muda nada e não gera auditoria.
    /// </summary>
    Task<CompetitionPublicationResult> SetPublishedAsync(
        Guid competitionId,
        bool published,
        string version,
        CancellationToken cancellationToken);
}

public enum CompetitionPublicationOutcome
{
    Completed,
    NotFound,
    Conflict,

    /// <summary>O checklist tem impedimentos; nada foi publicado.</summary>
    NotReady,
}

public sealed record CompetitionPublicationResult(
    CompetitionPublicationOutcome Outcome,
    CompetitionReadinessView? Readiness)
{
    public static CompetitionPublicationResult Of(CompetitionPublicationOutcome outcome) => new(outcome, null);
}

public sealed record CompetitionReadinessItemView(string Code, string Severity, string Message);

/// <summary>
/// O checklist com a situação do campeonato, o endereço público que ele ganhou ao ser
/// publicado e a versão que a publicação precisa devolver.
/// </summary>
public sealed record CompetitionReadinessView(
    Guid CompetitionId,
    string Status,
    DateTimeOffset? PublishedAt,
    string? Slug,
    bool CanPublish,
    IReadOnlyList<CompetitionReadinessItemView> Items,
    string Version)
{
    public static CompetitionReadinessView From(
        Competition competition,
        CompetitionReadinessReport report)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(report);

        return new(
            competition.Id,
            competition.Status.ToString(),
            competition.PublishedAt,
            competition.Slug,
            report.CanPublish,
            [.. report.Items.Select(item => new CompetitionReadinessItemView(
                item.Code,
                item.Severity.ToString(),
                item.Message))],
            Convert.ToBase64String(competition.RowVersion));
    }
}
