namespace Fut7Fantasy.Infrastructure.Persistence;

/// <summary>
/// Registro operacional de uma inicializacao da aplicacao. E um dado tecnico, nao de
/// dominio, por isso vive na Infrastructure e nao em Domain.
/// </summary>
public sealed class StartupRecord
{
    /// <summary>Identificador externo, GUID nao sequencial.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Versao informativa da aplicacao que iniciou.</summary>
    public required string Version { get; init; }

    /// <summary>Ambiente em que a aplicacao iniciou.</summary>
    public required string EnvironmentName { get; init; }

    /// <summary>Instante da inicializacao, sempre em UTC.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}
