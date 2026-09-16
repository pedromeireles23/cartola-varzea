using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Infrastructure.Identity;

/// <summary>Opcoes exclusivas do token de confirmacao de e-mail.</summary>
public sealed class EmailConfirmationTokenProviderOptions
{
    /// <summary>Tempo durante o qual o link pode ser usado.</summary>
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Provedor separado da recuperacao de senha, para que cada finalidade tenha sua
/// propria validade sem depender de opcoes nomeadas que o provedor padrao nao le.
/// </summary>
public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> emailOptions,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(
        dataProtectionProvider,
        Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
        {
            Name = "EmailConfirmationTokenProvider",
            TokenLifespan = emailOptions.Value.TokenLifespan,
        }),
        logger);
