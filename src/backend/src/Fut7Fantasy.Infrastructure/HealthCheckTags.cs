namespace Fut7Fantasy.Infrastructure;

/// <summary>Tags que separam liveness de readiness nos health checks.</summary>
public static class HealthCheckTags
{
    /// <summary>Checks que compoem a prontidao da aplicacao para atender requisicoes.</summary>
    public const string Readiness = "ready";
}
