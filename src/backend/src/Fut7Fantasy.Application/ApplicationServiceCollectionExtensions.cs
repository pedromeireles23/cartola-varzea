using Fut7Fantasy.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Fut7Fantasy.Application;

/// <summary>Registro dos casos de uso da camada Application.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adiciona os casos de uso ao contêiner.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<GetSystemInfo>();

        return services;
    }
}
