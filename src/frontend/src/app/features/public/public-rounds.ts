import { PublicFixture, PublicRound } from './public-fixture.service';

/**
 * Leituras do calendário público que a página do campeonato e a landing fazem do mesmo
 * jeito. Ficam aqui, como funções puras, para que as duas telas nunca discordem sobre
 * o que está acontecendo agora num campeonato.
 */

/**
 * As rodadas em andamento: toda rodada que ainda não fechou resultado, em ordem. Pode
 * ser mais de uma — a de ontem em conferência enquanto o mercado da próxima já abriu —,
 * e mostrar só a primeira escondia justamente a que a pessoa pode jogar. Quando todas
 * fecharam, não há "agora" a mostrar — quem manda na tela então é o último resultado.
 */
export function roundsInProgress(rounds: readonly PublicRound[]): readonly PublicRound[] {
  return rounds.filter(
    (round) =>
      round.underCorrection ||
      (round.phase !== 'Consolidated' &&
        round.phase !== 'Published' &&
        round.phase !== 'Cancelled' &&
        round.phase !== 'Draft'),
  );
}

/** O que vem por aí, no relógio de quem está lendo, do mais próximo ao mais distante. */
export function upcomingMatches(
  rounds: readonly PublicRound[],
  now: number,
  limit: number,
): readonly PublicFixture[] {
  return rounds
    .flatMap((round) => round.matches)
    .filter((match) => match.status === 'Scheduled' && Date.parse(match.kickoffAt) > now)
    .sort((one, other) => Date.parse(one.kickoffAt) - Date.parse(other.kickoffAt))
    .slice(0, limit);
}

/** A última rodada com resultado no ar; some enquanto ela está em correção. */
export function lastPublishedRound(rounds: readonly PublicRound[]): PublicRound | undefined {
  const published = rounds.filter((round) => round.resultPublished);
  return published.length > 0 ? published[published.length - 1] : undefined;
}
