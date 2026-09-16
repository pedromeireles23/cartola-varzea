using System.ComponentModel.DataAnnotations;

namespace Fut7Fantasy.Infrastructure.Options;

/// <summary>
/// Configuracao do banco. Validada na inicializacao: a aplicacao recusa subir sem
/// connection string, em vez de falhar na primeira requisicao.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Nome da secao de configuracao.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Connection string do SQL Server. Nunca fica versionada: em desenvolvimento vem de
    /// variavel de ambiente ou user-secrets, e em producao de Managed Identity/Key Vault.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Database:ConnectionString e obrigatoria.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout de comando, em segundos.</summary>
    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
