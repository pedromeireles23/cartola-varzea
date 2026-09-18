namespace Fut7Fantasy.Application.Importing;

/// <summary>
/// Estatísticas de uma rodada por CSV: todas as súmulas do domingo num arquivo só
/// (03 §13, ADR-009).
///
/// O placar não vem no arquivo; ele sai dos gols e gols contra, como no editor, e a
/// pré-visualização o mostra por jogo para a pessoa conferir. Um jogo que já tem súmula
/// é substituído, e a pré-visualização avisa disso antes.
/// </summary>
public interface IRoundStatisticsImportService
{
    /// <summary>
    /// Modelo preenchido com os jogos marcados da rodada e o elenco elegível de cada um,
    /// com o que já estiver lançado. Nulo quando a rodada não é deste campeonato.
    /// </summary>
    Task<RoundStatisticsFile?> TemplateAsync(
        Guid competitionId,
        Guid roundId,
        CancellationToken cancellationToken);

    Task<ImportResult> PreviewAsync(
        Guid competitionId,
        Guid roundId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>Revalida e grava todas as súmulas do arquivo, ou nenhuma.</summary>
    Task<ImportResult> CommitAsync(
        Guid competitionId,
        Guid roundId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

/// <param name="FileName">Nome sugerido para salvar, com a versão do modelo e a rodada.</param>
/// <param name="Content">CSV pronto, sem BOM; quem entrega o arquivo acrescenta.</param>
public sealed record RoundStatisticsFile(string FileName, string Content);
