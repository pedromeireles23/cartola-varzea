namespace Fut7Fantasy.Domain.Competitions;

/// <summary>Ciclo de vida do campeonato. Todo campeonato nasce em rascunho (01 §7).</summary>
public enum CompetitionStatus
{
    /// <summary>Visível só para a organização; tudo pode ser alterado.</summary>
    Draft = 1,

    /// <summary>Visível ao público; a modalidade fica travada.</summary>
    Published = 2,
}
