import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { AthletePosition } from '../organizer/competition-area/athlete.service';
import { ModalityProfile } from '../organizer/competition.service';

export type AssetKind = 'Athlete' | 'Coach';
export type SquadRole = 'Starter' | 'Bench' | 'Coach';

/**
 * Estado do mercado pelo relógio do servidor. `closesAt` é o instante em UTC, para a
 * contagem regressiva; `closesAtLocal`, o mesmo instante já no fuso do campeonato.
 */
export interface FantasyMarketStatus {
  readonly isOpen: boolean;
  readonly roundName: string | null;
  readonly closesAt: string | null;
  readonly closesAtLocal: string | null;
  readonly timeZoneId: string;
}

export interface FantasyTeamLimit {
  readonly activeRealTeams: number;
  readonly maxStarters: number;
  readonly maxAthletes: number;
}

export interface SquadSlot {
  readonly kind: AssetKind;
  readonly assetId: string;
  readonly name: string;
  readonly position: AthletePosition | null;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly role: SquadRole;
  readonly currentPrice: number;
  readonly purchasePrice: number;
  readonly isAvailable: boolean;
  readonly isCaptain: boolean;
}

export interface LineupIssue {
  readonly code: string;
  readonly message: string;
}

/** Vaga do retrato congelado: nome, time e preço como estavam no fechamento. */
export interface FrozenSlot {
  readonly kind: AssetKind;
  readonly assetId: string;
  readonly name: string;
  readonly position: AthletePosition | null;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly role: SquadRole;
  readonly price: number;
  readonly isCaptain: boolean;
}

/**
 * O que valeu na rodada mais recente com o mercado fechado: `Frozen` (a escalação
 * completa congelou e vale), `Incomplete` (faltava algo no fechamento) ou
 * `JoinedAfterClose` (a conta entrou depois). `slots` só vem em `Frozen`.
 */
export interface ClosedRoundLineup {
  readonly roundName: string;
  readonly marketClosedAt: string;
  readonly marketClosedAtLocal: string;
  readonly status: 'Frozen' | 'Incomplete' | 'JoinedAfterClose';
  readonly captainAthleteId: string | null;
  readonly slots: readonly FrozenSlot[];
}

export interface FantasyEntry {
  readonly balance: number;
  readonly patrimony: number;
  readonly captainAthleteId: string | null;
  readonly slots: readonly SquadSlot[];
  /** Vazia quando a escalação está completa. */
  readonly issues: readonly LineupIssue[];
  /** Nula enquanto nenhum mercado fechou. */
  readonly lastClosedRound: ClosedRoundLineup | null;
}

export interface FantasyOverview {
  readonly competitionName: string;
  readonly slug: string;
  readonly profile: ModalityProfile;
  readonly market: FantasyMarketStatus;
  readonly teamLimit: FantasyTeamLimit;
  /** Nula enquanto a conta não entrou no campeonato. */
  readonly entry: FantasyEntry | null;
}

/** Ativo à venda; `blockCode` e `blockReason` dizem por que não dá para comprar agora. */
export interface MarketItem {
  readonly kind: AssetKind;
  readonly id: string;
  readonly name: string;
  readonly position: AthletePosition | null;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly price: number;
  readonly isAvailable: boolean;
  readonly isOwned: boolean;
  readonly blockCode: string | null;
  readonly blockReason: string | null;
}

export interface FantasyMarket {
  readonly balance: number | null;
  readonly market: FantasyMarketStatus;
  readonly items: readonly MarketItem[];
}

/** Uma rodada já publicada, com a pontuação da conta; `total` nulo quando ela não jogou. */
export interface FantasyRoundSummary {
  readonly roundId: string;
  readonly roundName: string;
  readonly sequence: number;
  readonly publishedAt: string;
  readonly publishedAtLocal: string;
  readonly consolidatesAtLocal: string;
  readonly provisional: boolean;
  readonly timeZoneId: string;
  readonly total: number | null;
  /** O organizador reabriu a rodada: estes números ainda podem ser trocados. */
  readonly underCorrection: boolean;
}

/**
 * A correção que produziu a apuração vigente: quando ela saiu, por que a rodada foi
 * reaberta e quanto a conta tinha antes. `reason` é nulo quando a rodada foi corrigida
 * enquanto ainda era provisória, quando o motivo não é exigido.
 */
export interface FantasyRoundCorrection {
  readonly revision: number;
  readonly correctedAtLocal: string;
  readonly reason: string | null;
  readonly previousTotal: number | null;
}

/** Um item que gerou pontos: "2 gols, +12,00". */
export interface FantasyScoreLine {
  readonly item: string;
  readonly quantity: number;
  readonly points: number;
}

/** A variação de preço explicada pela média da posição (01 §9). */
export interface FantasyPriceChange {
  readonly average: number | null;
  readonly difference: number | null;
  readonly variation: number;
  readonly previousPrice: number;
  readonly newPrice: number;
}

export interface FantasyRoundSlot {
  readonly kind: AssetKind;
  readonly assetId: string;
  readonly name: string;
  readonly position: AthletePosition | null;
  readonly realTeamName: string;
  readonly role: SquadRole;
  readonly played: boolean;
  readonly points: number;
  /** Se os pontos entraram no total: titular que jogou, reserva que entrou, técnico com time em campo. */
  readonly counts: boolean;
  readonly isCaptain: boolean;
  /** Preenchido no reserva que entrou: o titular que ele cobriu. */
  readonly replaces: string | null;
  /** Preenchido no titular que não jogou: o reserva que entrou no lugar dele. */
  readonly replacedBy: string | null;
  readonly lines: readonly FantasyScoreLine[];
  readonly price: FantasyPriceChange | null;
}

/** `played` falso quer dizer que a conta não teve escalação congelada na rodada. */
export interface FantasyRoundScore {
  readonly roundId: string;
  readonly roundName: string;
  readonly publishedAtLocal: string;
  readonly consolidatesAtLocal: string;
  readonly provisional: boolean;
  readonly timeZoneId: string;
  readonly revision: number;
  readonly played: boolean;
  readonly total: number;
  readonly captainBonus: number;
  readonly slots: readonly FantasyRoundSlot[];
  readonly underCorrection: boolean;
  /** Nula na primeira apuração da rodada. */
  readonly correction: FantasyRoundCorrection | null;
}

export const FANTASY_MARKET_CLOSED_CODE = 'fantasy_market_closed';
export const FANTASY_NOT_JOINED_CODE = 'fantasy_not_joined';
export const FANTASY_CONFLICT_CODE = 'fantasy_conflict';

/** O jogo do participante num campeonato publicado, sempre pelo slug público. */
@Injectable({ providedIn: 'root' })
export class FantasyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  overview(slug: string): Observable<FantasyOverview> {
    return this.http.get<FantasyOverview>(`${this.url(slug)}/`);
  }

  /** Entrar de novo devolve a participação que já existe. */
  join(slug: string): Observable<FantasyOverview> {
    return this.http.post<FantasyOverview>(`${this.url(slug)}/entry`, null);
  }

  market(slug: string): Observable<FantasyMarket> {
    return this.http.get<FantasyMarket>(`${this.url(slug)}/market`);
  }

  buy(slug: string, kind: AssetKind, assetId: string): Observable<FantasyOverview> {
    return this.http.post<FantasyOverview>(this.squadUrl(slug, kind, assetId), null);
  }

  sell(slug: string, kind: AssetKind, assetId: string): Observable<FantasyOverview> {
    return this.http.delete<FantasyOverview>(this.squadUrl(slug, kind, assetId));
  }

  /** Titular vai para o banco e o reserva da mesma posição entra no lugar dele. */
  swap(
    slug: string,
    starterAthleteId: string,
    benchAthleteId: string,
  ): Observable<FantasyOverview> {
    return this.http.put<FantasyOverview>(`${this.url(slug)}/lineup/swap`, {
      starterAthleteId,
      benchAthleteId,
    });
  }

  /** Rodadas já publicadas, da mais recente para a mais antiga. */
  rounds(slug: string): Observable<FantasyRoundSummary[]> {
    return this.http.get<FantasyRoundSummary[]>(`${this.url(slug)}/rounds`);
  }

  round(slug: string, roundId: string): Observable<FantasyRoundScore> {
    return this.http.get<FantasyRoundScore>(`${this.url(slug)}/rounds/${roundId}`);
  }

  captain(slug: string, athleteId: string): Observable<FantasyOverview> {
    return this.http.put<FantasyOverview>(`${this.url(slug)}/lineup/captain`, { athleteId });
  }

  private squadUrl(slug: string, kind: AssetKind, assetId: string): string {
    return `${this.url(slug)}/squad/${kind === 'Coach' ? 'tecnico' : 'atleta'}/${assetId}`;
  }

  private url(slug: string): string {
    return `${this.baseUrl}/fantasy/${encodeURIComponent(slug)}`;
  }
}
