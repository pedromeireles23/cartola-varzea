using Fut7Fantasy.Domain.PlatformAdministration;
using Fut7Fantasy.Infrastructure.Identity;
using Fut7Fantasy.Infrastructure.Options;
using Fut7Fantasy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fut7Fantasy.Infrastructure.PlatformAdministration;

/// <summary>
/// Concede <c>PlatformAdmin</c> à conta do e-mail configurado em
/// <see cref="PlatformAdministrationOptions.InitialAdminEmail"/>.
///
/// Sem isso não existe caminho para o primeiro administrador: a fila de aprovação
/// exige um, e nenhum endpoint promove contas. A concessão só vale para e-mail
/// confirmado, para que cadastrar o endereço antes do dono não baste. Retirar o
/// e-mail da configuração não revoga o papel; a revogação é uma decisão explícita.
/// </summary>
public sealed class InitialPlatformAdmin(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    Fut7FantasyDbContext dbContext,
    TimeProvider clock,
    IOptions<PlatformAdministrationOptions> options,
    ILogger<InitialPlatformAdmin> logger)
{
    private readonly string? _email = options.Value.InitialAdminEmail;

    /// <summary>Indica se há um e-mail configurado.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_email);

    /// <summary>Procura a conta configurada e concede o papel quando ela já estiver confirmada.</summary>
    public async Task GrantToConfiguredAccountAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return;
        }

        var user = await users.FindByEmailAsync(_email!).ConfigureAwait(false);
        if (user is null)
        {
            PlatformAdministrationEvents.InitialAdminAccountMissing(logger);
            return;
        }

        await GrantIfConfiguredAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Concede o papel se a conta for a configurada, estiver confirmada e ainda não o
    /// tiver. Chamar de novo não repete a concessão nem a auditoria.
    /// </summary>
    public async Task<bool> GrantIfConfiguredAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!IsConfigured
            || !string.Equals(user.NormalizedEmail, users.NormalizeEmail(_email), StringComparison.Ordinal))
        {
            return false;
        }

        if (!user.EmailConfirmed)
        {
            PlatformAdministrationEvents.InitialAdminEmailNotConfirmed(logger, user.Id);
            return false;
        }

        if (await users.IsInRoleAsync(user, ApplicationRole.PlatformAdmin).ConfigureAwait(false))
        {
            return false;
        }

        if (!await roles.RoleExistsAsync(ApplicationRole.PlatformAdmin).ConfigureAwait(false))
        {
            EnsureSucceeded(await roles.CreateAsync(new ApplicationRole
            {
                Id = Guid.CreateVersion7(),
                Name = ApplicationRole.PlatformAdmin,
            }).ConfigureAwait(false));
        }

        // O UserStore grava o vínculo com SaveChanges no mesmo DbContext, então a
        // auditoria adicionada antes entra na mesma gravação que o papel.
        dbContext.AdministrativeAuditEntries.Add(AdministrativeAuditEntry.Create(
            user.Id,
            "PlatformAdminGrantedByConfiguration",
            user.Id,
            $"Concedido pela configuração {PlatformAdministrationOptions.SectionName}:"
                + nameof(PlatformAdministrationOptions.InitialAdminEmail) + ".",
            clock.GetUtcNow()));
        EnsureSucceeded(await users.AddToRoleAsync(user, ApplicationRole.PlatformAdmin).ConfigureAwait(false));

        // Mudança de privilégio rotaciona a sessão (04-seguranca §5).
        EnsureSucceeded(await users.UpdateSecurityStampAsync(user).ConfigureAwait(false));

        PlatformAdministrationEvents.InitialAdminGranted(logger, user.Id);
        return true;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Code)));
        }
    }
}
