namespace Fut7Fantasy.Application.Organizations;

/// <summary>Consultas de organização do ponto de vista da conta atual.</summary>
public interface IOrganizationService
{
    /// <summary>Organizações em que a conta atual é membro, com o papel de cada uma.</summary>
    Task<IReadOnlyList<MyOrganizationView>> GetMineAsync(CancellationToken cancellationToken);
}

public sealed record MyOrganizationView(
    Guid Id,
    string Name,
    string Role,
    DateTimeOffset JoinedAt);
