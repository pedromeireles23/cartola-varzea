using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fut7Fantasy.Demo;

/// <summary>
/// Recria o ambiente de demonstração do zero (Fase 12):
///
/// <c>dotnet run --project src/backend/src/Fut7Fantasy.Demo -- reset --banco Fut7Fantasy_Demo</c>
///
/// A connection string vem de <c>Database__ConnectionString</c> (ou user-secrets) e as
/// senhas das contas demo, de <c>Demo__ViewerPassword</c>, <c>Demo__OrganizerPassword</c>
/// e <c>Demo__AdminPassword</c>. O script <c>infra/scripts/demo-reset.ps1</c> prepara tudo
/// isso a partir do <c>.env</c>. Sem instruções de nível superior: a classe global
/// <c>Program</c> já é da API, e os testes referenciam os dois projetos.
/// </summary>
internal static class DemoProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 3 || args[0] != "reset" || args[1] != "--banco")
        {
            await Console.Error.WriteLineAsync("Uso: reset --banco <nome do banco da demo>").ConfigureAwait(false);
            return 2;
        }

        var builder = Host.CreateApplicationBuilder(args[3..]);
        builder.Configuration.AddUserSecrets<DemoSeeder>(optional: true);

        // O reset conta a história por centenas de comandos; o SQL de cada um só atrapalha a leitura.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        builder.Services.AddDemo(builder.Configuration, TimeProvider.System.GetUtcNow());

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<DemoSeeder>>();
        try
        {
            var summary = await DemoReset.RunAsync(host.Services, args[2], CancellationToken.None)
                .ConfigureAwait(false);
            DemoLog.Ready(logger, summary.Slug, summary.Entries, DemoUniverse.Viewer.Email);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // O console diz o que falhou em vez de derrubar com a pilha inteira.
            DemoLog.Failed(logger, exception.Message);
            return 1;
        }
    }
}
