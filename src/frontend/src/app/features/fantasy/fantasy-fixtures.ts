import { PERFIS } from '../organizer/competition-area/competition-fixtures';
import {
  FantasyEntry,
  FantasyRoundScore,
  FantasyRoundSlot,
  FantasyRoundSummary,
  FantasyMarket,
  FantasyMarketStatus,
  FantasyOverview,
  MarketItem,
  SquadSlot,
} from './fantasy.service';

/** Dados de teste das telas do jogo, no formato que a API devolve. */
export const SLUG = 'copa-da-varzea-2026';
export const OVERVIEW_URL = `/api/v1/fantasy/${SLUG}/`;
export const MARKET_URL = `/api/v1/fantasy/${SLUG}/market`;
export const LINEUP_URL = `/api/v1/fantasy/${SLUG}/lineup`;
export const ROUNDS_URL = `/api/v1/fantasy/${SLUG}/rounds`;
export const ROUND_ID = 'r1';
export const ROUND_URL = `${ROUNDS_URL}/${ROUND_ID}`;

export function rodadaResumo(parcial: Partial<FantasyRoundSummary> = {}): FantasyRoundSummary {
  return {
    roundId: ROUND_ID,
    roundName: 'Rodada 1',
    sequence: 1,
    publishedAt: '2026-09-21T15:00:00Z',
    publishedAtLocal: '2026-09-21T12:00',
    consolidatesAtLocal: '2026-09-23T10:00',
    provisional: true,
    timeZoneId: 'America/Sao_Paulo',
    total: 35,
    underCorrection: false,
    ...parcial,
  };
}

export function vagaApurada(parcial: Partial<FantasyRoundSlot> = {}): FantasyRoundSlot {
  return {
    kind: 'Athlete',
    assetId: 'a1',
    name: 'Bia',
    position: 'Midfielder',
    realTeamName: 'União da Vila',
    role: 'Starter',
    played: true,
    points: 8,
    counts: true,
    isCaptain: false,
    replaces: null,
    replacedBy: null,
    lines: [],
    price: null,
    ...parcial,
  };
}

export function rodadaApurada(parcial: Partial<FantasyRoundScore> = {}): FantasyRoundScore {
  return {
    roundId: ROUND_ID,
    roundName: 'Rodada 1',
    publishedAtLocal: '2026-09-21T12:00',
    consolidatesAtLocal: '2026-09-23T10:00',
    provisional: true,
    timeZoneId: 'America/Sao_Paulo',
    revision: 1,
    played: true,
    total: 35,
    captainBonus: 8,
    slots: [vagaApurada()],
    underCorrection: false,
    correction: null,
    ...parcial,
  };
}

export function mercadoAberto(parcial: Partial<FantasyMarketStatus> = {}): FantasyMarketStatus {
  return {
    isOpen: true,
    roundName: 'Rodada 1',
    closesAt: '2099-09-20T22:00:00Z',
    closesAtLocal: '2099-09-20T19:00',
    timeZoneId: 'America/Sao_Paulo',
    ...parcial,
  };
}

export const MERCADO_FECHADO: FantasyMarketStatus = {
  isOpen: false,
  roundName: null,
  closesAt: null,
  closesAtLocal: null,
  timeZoneId: 'America/Sao_Paulo',
};

export function entrada(parcial: Partial<FantasyEntry> = {}): FantasyEntry {
  return {
    balance: 100,
    patrimony: 100,
    captainAthleteId: null,
    slots: [],
    issues: [
      { code: 'missing_starter', message: 'Falta 1 goleiro entre os titulares.' },
      { code: 'missing_coach', message: 'Falta o técnico.' },
    ],
    lastClosedRound: null,
    ...parcial,
  };
}

export function vaga(parcial: Partial<SquadSlot> = {}): SquadSlot {
  return {
    kind: 'Athlete',
    assetId: parcial.name ?? 'a1',
    name: 'Bia',
    position: 'Midfielder',
    realTeamId: 't1',
    realTeamName: 'União da Vila',
    role: 'Starter',
    currentPrice: 8,
    purchasePrice: 8,
    isAvailable: true,
    isCaptain: false,
    ...parcial,
  };
}

export function visao(parcial: Partial<FantasyOverview> = {}): FantasyOverview {
  return {
    competitionName: 'Copa da Várzea',
    slug: SLUG,
    profile: PERFIS[0]!,
    market: mercadoAberto(),
    teamLimit: { activeRealTeams: 4, maxStarters: 3, maxAthletes: 5 },
    entry: entrada(),
    ...parcial,
  };
}

export function item(parcial: Partial<MarketItem> = {}): MarketItem {
  return {
    kind: 'Athlete',
    id: 'a1',
    name: 'Bia',
    position: 'Midfielder',
    realTeamId: 't1',
    realTeamName: 'União da Vila',
    price: 8,
    isAvailable: true,
    isOwned: false,
    blockCode: null,
    blockReason: null,
    ...parcial,
  };
}

export function mercado(itens: MarketItem[], parcial: Partial<FantasyMarket> = {}): FantasyMarket {
  return { balance: 100, market: mercadoAberto(), items: itens, ...parcial };
}
