using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>Parâmetros versionados do módulo de organizações.</summary>
public sealed class OrganizationOptions
{
    public const string SectionName = "Organizations";

    /// <summary>Tempo disponível para aceitar um convite de auxiliar.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "30.00:00:00")]
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);
}
