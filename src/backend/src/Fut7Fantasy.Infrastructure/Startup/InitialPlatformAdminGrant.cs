using Fut7Fantasy.Infrastructure.PlatformAdministration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Infrastructure.Startup;

/// <summary>
/// Concede o administrador inicial ao subir, para a conta que já existia e já estava
/// confirmada. Contas confirmadas depois recebem o papel na própria confirmação.
/// Assim como o <see cref="StartupRecorder"/>, falha de banco não derruba o processo.
/// </summary>
public sealed class InitialPlatformAdminGrant(
    IServiceScopeFactory scopeFactory,
    ILogger<InitialPlatformAdminGrant> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var initialAdmin = scope.ServiceProvider.GetRequiredService<InitialPlatformAdmin>();
        if (!initialAdmin.IsConfigured)
        {
            return;
        }

        try
        {
            await initialAdmin.GrantToConfiguredAccountAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A concessão é repetida no próximo início ou na confirmação do e-mail.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            PlatformAdministrationEvents.InitialAdminGrantFailed(logger, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
