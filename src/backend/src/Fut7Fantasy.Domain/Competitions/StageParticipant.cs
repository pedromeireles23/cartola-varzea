namespace Fut7Fantasy.Domain.Competitions;

/// <summary>
/// Participação de um time real em uma fase. Em fases de grupos aponta para um grupo
/// da própria fase; no mata-mata, <see cref="StageGroupId"/> fica vazio.
/// </summary>
public sealed class StageParticipant
{
    private StageParticipant()
    {
    }

    private StageParticipant(
        Guid id,
        Guid stageId,
        Guid realTeamId,
        Guid? stageGroupId,
        DateTimeOffset createdAt)
    {
        Id = id;
        StageId = stageId;
        RealTeamId = realTeamId;
        StageGroupId = stageGroupId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid StageId { get; private set; }

    public Guid RealTeamId { get; private set; }

    public Guid? StageGroupId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static StageParticipant Create(
        Guid id,
        Guid stageId,
        Guid realTeamId,
        Guid? stageGroupId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || stageId == Guid.Empty || realTeamId == Guid.Empty)
        {
            throw new ArgumentException("Participação, fase e time precisam ser identificados.");
        }

        if (stageGroupId == Guid.Empty)
        {
            throw new ArgumentException("O grupo informado precisa ser identificado.", nameof(stageGroupId));
        }

        return new StageParticipant(id, stageId, realTeamId, stageGroupId, createdAt);
    }

    public void AssignToGroup(Guid? stageGroupId, DateTimeOffset updatedAt)
    {
        if (stageGroupId == Guid.Empty)
        {
            throw new ArgumentException("O grupo informado precisa ser identificado.", nameof(stageGroupId));
        }

        StageGroupId = stageGroupId;
        UpdatedAt = updatedAt;
    }
}
