using Fut7Fantasy.Domain.Competitions;

namespace Fut7Fantasy.Application.Competitions;

/// <summary>
/// Fases de um campeonato. A policy de campeonato já validou o escopo; toda consulta
/// ainda filtra pelo campeonato da rota, para que uma fase de outro campeonato nunca
/// seja encontrada pelo Id.
/// </summary>
public interface ICompetitionStageService
{
    Task<IReadOnlyList<StageView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);

    /// <summary>Acrescenta a fase ao fim da ordem.</summary>
    Task<StageCommandResult> CreateAsync(
        Guid competitionId,
        StageDefinition definition,
        CancellationToken cancellationToken);

    Task<StageCommandResult> UpdateAsync(
        Guid competitionId,
        Guid stageId,
        StageDefinition definition,
        string version,
        CancellationToken cancellationToken);

    /// <summary>Remove a fase e renumera as seguintes.</summary>
    Task<StageCommandOutcome> DeleteAsync(Guid competitionId, Guid stageId, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica a ordem informada. A lista precisa ter exatamente as fases atuais: se alguém
    /// criou ou removeu uma fase depois da leitura, o resultado é conflito.
    /// </summary>
    Task<StageListResult> ReorderAsync(
        Guid competitionId,
        IReadOnlyList<Guid> stageIds,
        CancellationToken cancellationToken);
}

public enum StageCommandOutcome
{
    Completed,
    Invalid,
    NotFound,
    Conflict,
    LimitReached,
}

public sealed record StageCommandResult(
    StageCommandOutcome Outcome,
    StageView? Stage,
    IReadOnlyList<CompetitionSettingsError> Errors)
{
    public static StageCommandResult Of(StageCommandOutcome outcome) => new(outcome, null, []);
}

public sealed record StageListResult(StageCommandOutcome Outcome, IReadOnlyList<StageView> Stages);

/// <summary>Fase com grupos em ordem e a versão que a edição precisa devolver.</summary>
public sealed record StageView(
    Guid Id,
    string Name,
    string Format,
    int Sequence,
    IReadOnlyList<StageGroupView> Groups,
    IReadOnlyList<string> Tiebreakers,
    string Version)
{
    public static StageView From(Stage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return new StageView(
            stage.Id,
            stage.Name,
            stage.Format.ToString(),
            stage.Sequence,
            [.. stage.Groups.Select(group => new StageGroupView(group.Id, group.Name))],
            [.. stage.Tiebreakers.Select(criterion => criterion.ToString())],
            Convert.ToBase64String(stage.RowVersion));
    }
}

public sealed record StageGroupView(Guid Id, string Name);
