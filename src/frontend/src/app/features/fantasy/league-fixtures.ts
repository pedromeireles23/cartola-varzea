import { League, LeagueMember, LeagueSummary } from './league.service';

/** Dados das telas de liga nos testes; o mesmo campeonato das outras fixtures. */
export const SLUG = 'copa-da-vila';
export const LIGA_ID = '11111111-1111-1111-1111-111111111111';
export const LEAGUES_URL = `/api/v1/fantasy/${SLUG}/leagues`;
export const LEAGUE_URL = `${LEAGUES_URL}/${LIGA_ID}`;

/** Dez caracteres do alfabeto de `LeagueInviteCode`, sem os que se confundem. */
export const CODIGO = 'ABCDE2345K';

export function resumo(changes: Partial<LeagueSummary> = {}): LeagueSummary {
  return {
    id: LIGA_ID,
    name: 'Turma do sábado',
    competitionSlug: SLUG,
    members: 4,
    isOwner: false,
    position: 2,
    inviteCode: null,
    inviteExpiresAtLocal: null,
    version: 'v1',
    ...changes,
  };
}

export function membro(changes: Partial<LeagueMember> = {}): LeagueMember {
  return {
    membershipId: null,
    position: 1,
    tied: false,
    displayName: 'Pessoa Um',
    totalPoints: 46,
    netWorth: 104.5,
    lastRoundPoints: 16,
    isViewer: false,
    isOwner: false,
    ...changes,
  };
}

export function liga(changes: Partial<League> = {}): League {
  return {
    id: LIGA_ID,
    name: 'Turma do sábado',
    competitionName: 'Copa da Vila',
    competitionSlug: SLUG,
    isOwner: false,
    inviteCode: null,
    inviteExpiresAtLocal: null,
    rounds: 2,
    lastRoundName: 'Rodada 2',
    provisional: false,
    members: [membro()],
    version: 'v1',
    ...changes,
  };
}
