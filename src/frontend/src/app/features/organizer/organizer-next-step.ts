import { MyCompetition, shortLocalText } from './competition.service';
import { ROUND_NEEDS_REVIEW, ROUND_NEXT_STEP } from './competition-area/round.service';

/** O que um campeonato pede agora, dito para quem organiza, com o atalho para fazer. */
export interface NextStep {
  readonly texto: string;
  readonly rotulo: string;
  readonly link: readonly string[];
  /** Pede ação da organização agora: súmula para lançar ou conferir. */
  readonly urgente: boolean;
}

/**
 * O próximo passo de um campeonato em "Meus campeonatos". É a mesma leitura da Central
 * da rodada, levada para fora do campeonato: no rascunho, a montagem; publicado, a
 * rodada em jogo — súmula antes de tudo —, depois a próxima a abrir o mercado, e por
 * fim a tabela, quando não há mais rodada por vir. O auxiliar recebe o mesmo texto, mas
 * o atalho dele nunca leva a uma tela em que só o proprietário age.
 */
export function nextStep(campeonato: MyCompetition): NextStep {
  const base = ['/organizar/c', campeonato.id];
  const dono = campeonato.viewerRole === 'Owner';
  const rodadas = [...base, 'rodadas'];

  if (campeonato.status === 'Draft') {
    const montado = campeonato.activeTeams >= 2 && campeonato.matches > 0;
    if (!dono) {
      return {
        texto: 'Em montagem pela organização.',
        rotulo: 'Abrir o resumo',
        link: base,
        urgente: false,
      };
    }
    return montado
      ? {
          texto: `${contagem(campeonato.activeTeams, 'time', 'times')} e ${contagem(campeonato.matches, 'jogo', 'jogos')} marcados: confira as pendências e publique.`,
          rotulo: 'Revisar e publicar',
          link: [...base, 'montar', 'publicar'],
          urgente: false,
        }
      : {
          texto: `Em montagem: ${contagem(campeonato.activeTeams, 'time', 'times')} e ${contagem(campeonato.matches, 'jogo', 'jogos')} até agora.`,
          rotulo: 'Continuar a montagem',
          link: [...base, 'montar'],
          urgente: false,
        };
  }

  const atual = campeonato.currentRound;
  if (atual) {
    const revisar = ROUND_NEEDS_REVIEW.includes(atual.phase);
    const passo = ROUND_NEXT_STEP[atual.phase] ?? '';
    const fechamento =
      atual.phase === 'MarketOpen' && atual.marketCloseLocal
        ? ` Fecha em ${shortLocalText(atual.marketCloseLocal)}.`
        : '';
    return {
      texto: `${atual.name}. ${passo}${fechamento}`,
      rotulo: revisar ? 'Revisar rodada' : 'Ver rodadas',
      link: revisar ? [...rodadas, atual.id, 'revisao'] : rodadas,
      urgente: revisar,
    };
  }

  const proxima = campeonato.nextRound;
  if (proxima) {
    const inicio = proxima.firstKickoffLocal
      ? `, com o primeiro jogo em ${shortLocalText(proxima.firstKickoffLocal)}`
      : '';
    return {
      texto: `Próxima: ${proxima.name}${inicio}. O mercado abre quando a organização quiser.`,
      rotulo: dono ? 'Abrir o mercado' : 'Ver rodadas',
      link: rodadas,
      urgente: false,
    };
  }

  if (campeonato.matches === 0) {
    return {
      texto: 'Publicado, mas ainda sem jogos marcados.',
      rotulo: dono ? 'Gerar a tabela' : 'Ver rodadas',
      link: dono ? [...base, 'montar', 'tabela'] : rodadas,
      urgente: false,
    };
  }

  return {
    texto:
      'Nenhuma rodada por vir. Se houver fase seguinte, confirme os classificados e gere os jogos.',
    rotulo: dono ? 'Ir para a tabela' : 'Ver rodadas',
    link: dono ? [...base, 'montar', 'tabela'] : rodadas,
    urgente: false,
  };
}

function contagem(total: number, singular: string, plural: string): string {
  return `${total} ${total === 1 ? singular : plural}`;
}
