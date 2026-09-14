using System.Reflection;

namespace Fut7Fantasy.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    private static readonly string[] FrameworkPrefixesForbiddenInCore =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Azure",
    ];

    [Fact]
    public void DomainDoesNotReferenceOtherLayers()
    {
        var references = ProjectReferences(Domain);

        Assert.Empty(references);
    }

    // O compilador só grava referências a assemblies realmente usados, por isso as regras são negativas.
    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrApi()
    {
        var references = ProjectReferences(Application);

        Assert.DoesNotContain("Fut7Fantasy.Infrastructure", references);
        Assert.DoesNotContain("Fut7Fantasy.Api", references);
    }

    [Fact]
    public void InfrastructureDoesNotReferenceApi()
    {
        var references = ProjectReferences(Infrastructure);

        Assert.DoesNotContain("Fut7Fantasy.Api", references);
    }

    [Theory]
    [InlineData("Fut7Fantasy.Domain")]
    [InlineData("Fut7Fantasy.Application")]
    public void CoreLayersDoNotReferenceWebOrPersistenceFrameworks(string assemblyName)
    {
        var assembly = assemblyName == "Fut7Fantasy.Domain" ? Domain : Application;

        var forbidden = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => FrameworkPrefixesForbiddenInCore.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(forbidden);
    }

    private static string[] ProjectReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Fut7Fantasy.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
