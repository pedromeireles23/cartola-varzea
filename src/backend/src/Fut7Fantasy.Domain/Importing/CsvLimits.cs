namespace Fut7Fantasy.Domain.Importing;

/// <summary>
/// Tetos da importação CSV (04 §9). Existem para que um arquivo hostil, ou só grande
/// demais, seja recusado antes de consumir memória e tempo do servidor.
/// </summary>
public static class CsvLimits
{
    /// <summary>1 MiB comporta com folga um campeonato inteiro de várzea em texto.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Linhas de dados, sem contar o cabeçalho.</summary>
    public const int MaxRows = 2_000;

    /// <summary>Nenhum template chega perto disso; colunas extras são erro, não sobra.</summary>
    public const int MaxColumns = 20;

    /// <summary>Uma célula maior que isto não é um nome: é conteúdo colado por engano.</summary>
    public const int MaxCellLength = 200;
}
