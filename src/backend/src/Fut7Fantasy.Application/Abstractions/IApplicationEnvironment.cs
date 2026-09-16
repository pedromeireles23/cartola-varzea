namespace Fut7Fantasy.Application.Abstractions;

/// <summary>
/// Dados do processo em execucao. Existe para que a camada Application nao dependa
/// do host ASP.NET Core.
/// </summary>
public interface IApplicationEnvironment
{
    /// <summary>Nome do ambiente: Development, Production e afins.</summary>
    string EnvironmentName { get; }

    /// <summary>Versao informativa do assembly da aplicacao.</summary>
    string Version { get; }
}
