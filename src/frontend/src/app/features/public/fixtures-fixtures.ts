import {
  PublicFixture,
  PublicFixtures,
  PublicMatch,
  PublicMatchAthlete,
  PublicRound,
} from './public-fixture.service';

/** Dados das telas de calendário e súmula nos testes. */
export const SLUG = 'copa-da-vila';
export const FIXTURES_URL = `/api/v1/public/competitions/${SLUG}/fixtures`;
export const PARTIDA_ID = '44444444-4444-4444-4444-444444444444';
export const MATCH_URL = `/api/v1/public/competitions/${SLUG}/matches/${PARTIDA_ID}`;

export function jogo(changes: Partial<PublicFixture> = {}): PublicFixture {
  return {
    id: PARTIDA_ID,
    stageName: 'Fase única',
    homeTeamName: 'União da Vila',
    awayTeamName: 'Estrela do Bairro',
    kickoffAt: '2026-09-20T13:00:00Z',
    kickoffLocal: '2026-09-20T10:00',
    status: 'Scheduled',
    homeScore: null,
    awayScore: null,
    hasSheet: false,
    ...changes,
  };
}

export function rodada(changes: Partial<PublicRound> = {}): PublicRound {
  return {
    id: '55555555-5555-5555-5555-555555555555',
    name: 'Rodada 1',
    sequence: 1,
    phase: 'MarketOpen',
    resultPublished: false,
    underCorrection: false,
    provisional: false,
    matches: [jogo()],
    ...changes,
  };
}

export function calendario(changes: Partial<PublicFixtures> = {}): PublicFixtures {
  return {
    slug: SLUG,
    name: 'Copa da Vila',
    timeZoneId: 'America/Sao_Paulo',
    rounds: [rodada()],
    ...changes,
  };
}

export function atleta(changes: Partial<PublicMatchAthlete> = {}): PublicMatchAthlete {
  return {
    sportingName: 'Pedrinho',
    position: 'Forward',
    playedAsGoalkeeper: false,
    goals: 0,
    assists: 0,
    goalkeeperSaves: 0,
    penaltySaves: 0,
    yellowCards: 0,
    redCards: 0,
    ownGoals: 0,
    penaltyMisses: 0,
    goalsConceded: 0,
    ...changes,
  };
}

export function partida(changes: Partial<PublicMatch> = {}): PublicMatch {
  return {
    id: PARTIDA_ID,
    slug: SLUG,
    competitionName: 'Copa da Vila',
    roundName: 'Rodada 1',
    stageName: 'Fase única',
    homeTeamName: 'União da Vila',
    awayTeamName: 'Estrela do Bairro',
    homeScore: 2,
    awayScore: 0,
    kickoffLocal: '2026-09-20T10:00',
    provisional: false,
    teams: [
      { name: 'União da Vila', isHome: true, athletes: [atleta({ goals: 2 })] },
      { name: 'Estrela do Bairro', isHome: false, athletes: [atleta({ sportingName: 'Juca' })] },
    ],
    ...changes,
  };
}
