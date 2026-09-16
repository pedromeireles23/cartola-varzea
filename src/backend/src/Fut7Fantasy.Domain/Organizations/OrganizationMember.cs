namespace Fut7Fantasy.Domain.Organizations;

/// <summary>Concede um papel a uma conta somente dentro de uma organização.</summary>
public sealed class OrganizationMember
{
    private OrganizationMember()
    {
    }

    private OrganizationMember(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset joinedAt)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public OrganizationRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>Cria o primeiro membro, proprietário da organização.</summary>
    public static OrganizationMember CreateOwner(Guid organizationId, Guid userId, DateTimeOffset joinedAt)
        => Create(organizationId, userId, OrganizationRole.Owner, joinedAt);

    /// <summary>Cria uma associação auxiliar a partir de um convite aceito.</summary>
    public static OrganizationMember CreateAssistant(Guid organizationId, Guid userId, DateTimeOffset joinedAt)
        => Create(organizationId, userId, OrganizationRole.Assistant, joinedAt);

    private static OrganizationMember Create(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset joinedAt)
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Organização e conta precisam ser identificadas.");
        }

        return new OrganizationMember(organizationId, userId, role, joinedAt);
    }
}
