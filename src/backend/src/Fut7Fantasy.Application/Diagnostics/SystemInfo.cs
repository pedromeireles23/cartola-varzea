namespace Fut7Fantasy.Application.Diagnostics;

/// <summary>
/// Informacao tecnica publica da aplicacao. Nao expoe connection string, versao de
/// dependencia, caminho de arquivo nem qualquer dado que ajude a mapear a infraestrutura.
/// </summary>
/// <param name="Version">Versao informativa da aplicacao.</param>
/// <param name="Environment">Nome do ambiente em execucao.</param>
/// <param name="ServerTimeUtc">Instante atual segundo o relogio do servidor, sempre em UTC.</param>
/// <param name="StartupCount">Quantidade de inicializacoes registradas no banco.</param>
/// <param name="LastStartedAt">Instante da ultima inicializacao registrada.</param>
public sealed record SystemInfo(
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc,
    int StartupCount,
    DateTimeOffset? LastStartedAt);
