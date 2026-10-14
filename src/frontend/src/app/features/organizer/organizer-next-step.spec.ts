import { MyCompetition, MyCompetitionRound } from './competition.service';
import { nextStep } from './organizer-next-step';

function campeonato(parcial: Partial<MyCompetition> = {}): MyCompetition {
  return {
    id: 'c1',
    name: 'Copa da Vila',
    season: '2026',
    modality: 'Fut7',
    status: 'Published',
    updatedAt: '2026-10-14T12:00:00Z',
    organizationId: 'o1',
    organizationName: 'Liga do Leste',
    viewerRole: 'Owner',
    activeTeams: 6,
    matches: 12,
    currentRound: null,
    nextRound: null,
    ...parcial,
  };
}

function rodada(parcial: Partial<MyCompetitionRound>): MyCompetitionRound {
  return {
    id: 'r4',
    name: 'Rodada 4',
    phase: 'Draft',
    matches: 3,
    firstKickoffLocal: '2026-10-18T09:00',
    marketCloseLocal: null,
    ...parcial,
  };
}

describe('nextStep', () => {
  it('no rascunho, leva à montagem até haver times e jogos; depois, à publicação', () => {
    const vazio = nextStep(campeonato({ status: 'Draft', activeTeams: 0, matches: 0 }));
    expect(vazio).toEqual({
      texto: 'Em montagem: 0 times e 0 jogos até agora.',
      rotulo: 'Continuar a montagem',
      link: ['/organizar/c', 'c1', 'montar'],
      urgente: false,
    });

    const pronto = nextStep(campeonato({ status: 'Draft', activeTeams: 6, matches: 1 }));
    expect(pronto.texto).toBe('6 times e 1 jogo marcados: confira as pendências e publique.');
    expect(pronto.link).toEqual(['/organizar/c', 'c1', 'montar', 'publicar']);
  });

  it('o auxiliar não é levado à montagem, que é do proprietário', () => {
    const passo = nextStep(campeonato({ status: 'Draft', viewerRole: 'Assistant' }));

    expect(passo.rotulo).toBe('Abrir o resumo');
    expect(passo.link).toEqual(['/organizar/c', 'c1']);
  });

  it('súmula para conferir vem antes de tudo e leva à Central da rodada', () => {
    const passo = nextStep(
      campeonato({
        currentRound: rodada({ phase: 'UnderReview' }),
        nextRound: rodada({ id: 'r5', name: 'Rodada 5' }),
      }),
    );

    expect(passo).toEqual({
      texto: 'Rodada 4. Em conferência: confira as súmulas e publique o resultado.',
      rotulo: 'Revisar rodada',
      link: ['/organizar/c', 'c1', 'rodadas', 'r4', 'revisao'],
      urgente: true,
    });
  });

  it('com o mercado aberto, diz quando fecha', () => {
    const passo = nextStep(
      campeonato({
        currentRound: rodada({ phase: 'MarketOpen', marketCloseLocal: '2026-10-18T08:00' }),
      }),
    );

    expect(passo.texto).toBe(
      'Rodada 4. Mercado aberto: quem joga ainda mexe na escalação até o fechamento. Fecha em 18/10 08:00.',
    );
    expect(passo.rotulo).toBe('Ver rodadas');
    expect(passo.urgente).toBe(false);
  });

  it('sem rodada em jogo, aponta a próxima a abrir o mercado', () => {
    const passo = nextStep(campeonato({ nextRound: rodada({ name: 'Rodada 5' }) }));

    expect(passo.texto).toBe(
      'Próxima: Rodada 5, com o primeiro jogo em 18/10 09:00. O mercado abre quando a organização quiser.',
    );
    expect(passo.rotulo).toBe('Abrir o mercado');
    expect(passo.link).toEqual(['/organizar/c', 'c1', 'rodadas']);
    expect(nextStep(campeonato({ viewerRole: 'Assistant', nextRound: rodada({}) })).rotulo).toBe(
      'Ver rodadas',
    );
  });

  it('sem rodada por vir, leva à tabela: sem jogo ainda, ou para a fase seguinte', () => {
    expect(nextStep(campeonato({ matches: 0 }))).toEqual({
      texto: 'Publicado, mas ainda sem jogos marcados.',
      rotulo: 'Gerar a tabela',
      link: ['/organizar/c', 'c1', 'montar', 'tabela'],
      urgente: false,
    });
    expect(nextStep(campeonato()).rotulo).toBe('Ir para a tabela');
    expect(nextStep(campeonato({ viewerRole: 'Assistant' })).link).toEqual([
      '/organizar/c',
      'c1',
      'rodadas',
    ]);
  });
});
