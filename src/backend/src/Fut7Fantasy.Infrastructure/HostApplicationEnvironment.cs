using System.Reflection;
using Fut7Fantasy.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Fut7Fantasy.Infrastructure;

/// <summary>Le ambiente e versao do host que executa a aplicacao.</summary>
public sealed class HostApplicationEnvironment(IHostEnvironment hostEnvironment) : IApplicationEnvironment
{
    private static readonly string AssemblyVersion =
        typeof(HostApplicationEnvironment).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

    /// <inheritdoc />
    public string EnvironmentName => hostEnvironment.EnvironmentName;

    /// <inheritdoc />
    // O sufixo de metadados do build (+sha) identifica o commit e nao interessa ao cliente.
    public string Version => AssemblyVersion.Split('+', 2)[0];
}
