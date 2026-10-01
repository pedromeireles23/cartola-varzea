import { PublicFixture, PublicRound } from './public-fixture.service';
import { lastPublishedRound, roundsInProgress, upcomingMatches } from './public-rounds';

function jogo(id: string, kickoffAt: string, changes: Partial<PublicFixture> = {}): PublicFixture {
  return {
    id,
    stageName: 'Fase única',
    homeTeamName: 'Casa',
    awayTeamName: 'Visitante',
    kickoffAt,
    kickoffLocal: kickoffAt,
    status: 'Scheduled',
    homeScore: null,
    awayScore: null,
    hasSheet: false,
    ...changes,
  };
}

function rodada(id: string, changes: Partial<PublicRound> = {}): PublicRound {
  return {
    id,
    name: id,
    sequence: 1,
    phase: 'Published',
    resultPublished: true,
    underCorrection: false,
    provisional: false,
    matches: [],
    ...changes,
  };
}

describe('leituras do calendário público', () => {
  it('em andamento é toda rodada sem resultado fechado, inclusive a que está em correção', () => {
    const rodadas = [
      rodada('rascunho', { phase: 'Draft', resultPublished: false }),
      rodada('consolidada', { phase: 'Consolidated' }),
      rodada('publicada'),
      rodada('cancelada', { phase: 'Cancelled', resultPublished: false }),
      rodada('conferencia', { phase: 'UnderReview', resultPublished: false }),
      rodada('mercado', { phase: 'MarketOpen', resultPublished: false }),
      rodada('correcao', {
        phase: 'ReopenedForCorrection',
        resultPublished: false,
        underCorrection: true,
      }),
    ];

    expect(roundsInProgress(rodadas).map((item) => item.id)).toEqual([
      'conferencia',
      'mercado',
      'correcao',
    ]);
  });

  it('próximos jogos são só os marcados e futuros, do mais próximo, até o limite', () => {
    const agora = Date.parse('2026-09-20T12:00:00Z');
    const rodadas = [
      rodada('r1', {
        matches: [
          jogo('passado', '2026-09-19T12:00:00Z'),
          jogo('depois', '2026-09-23T12:00:00Z'),
          jogo('adiado', '2026-09-21T12:00:00Z', { status: 'Postponed' }),
        ],
      }),
      rodada('r2', {
        matches: [jogo('logo', '2026-09-21T12:00:00Z'), jogo('fim', '2026-09-30T12:00:00Z')],
      }),
    ];

    expect(upcomingMatches(rodadas, agora, 2).map((item) => item.id)).toEqual(['logo', 'depois']);
  });

  it('o último resultado é a última rodada publicada; sem nenhuma, não há', () => {
    expect(
      lastPublishedRound([
        rodada('r1'),
        rodada('r2'),
        rodada('r3', { phase: 'MarketOpen', resultPublished: false }),
      ])?.id,
    ).toBe('r2');
    expect(lastPublishedRound([rodada('r1', { resultPublished: false })])).toBeUndefined();
  });
});
