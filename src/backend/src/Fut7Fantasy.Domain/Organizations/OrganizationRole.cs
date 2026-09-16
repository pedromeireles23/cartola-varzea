namespace Fut7Fantasy.Domain.Organizations;

/// <summary>Papel de uma pessoa dentro de uma organização específica.</summary>
public enum OrganizationRole
{
    /// <summary>Responsável pela organização e por suas permissões.</summary>
    Owner = 1,

    /// <summary>Auxilia a operação, sem administrar permissões.</summary>
    Assistant = 2,
}
