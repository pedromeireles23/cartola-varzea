namespace Fut7Fantasy.Domain.Organizations;

/// <summary>Fronteira administrativa que é proprietária dos campeonatos.</summary>
public sealed class Organization
{
    private Organization()
    {
    }

    private Organization(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Cria uma organização com nome normalizado.</summary>
    public static Organization Create(Guid id, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Organization(id, name.Trim(), createdAt);
    }
}
