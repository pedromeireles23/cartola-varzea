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

export interface FantasyEntry {
  readonly balance: number;
  readonly patrimony: number;
  readonly captainAthleteId: string | null;
  readonly slots: readonly SquadSlot[];
  /** Vazia quando a escalação está completa. */
  readonly issues: readonly LineupIssue[];
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

  private squadUrl(slug: string, kind: AssetKind, assetId: string): string {
    return `${this.url(slug)}/squad/${kind === 'Coach' ? 'tecnico' : 'atleta'}/${assetId}`;
  }

  private url(slug: string): string {
    return `${this.baseUrl}/fantasy/${encodeURIComponent(slug)}`;
  }
}
