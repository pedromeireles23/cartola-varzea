import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

/** As fases da rodada como o servidor as nomeia (01 §8). */
export type RoundPhase =
  | 'Draft'
  | 'MarketOpen'
  | 'MarketClosed'
  | 'InProgress'
  | 'UnderReview'
  | 'Published'
  | 'Consolidated'
  | 'Cancelled'
  | 'ReopenedForCorrection';

export type MatchStatus = 'Scheduled' | 'Postponed' | 'Cancelled';

/**
 * Uma partida no calendário. `homeScore` e `awayScore` são nulos enquanto a rodada não
 * publica: fato em edição não é fato público.
 */
export interface PublicFixture {
  readonly id: string;
  readonly stageName: string;
  readonly homeTeamName: string;
  readonly awayTeamName: string;
  readonly kickoffAt: string;
  readonly kickoffLocal: string;
  readonly status: MatchStatus;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly hasSheet: boolean;
}

export interface PublicRound {
  readonly id: string;
  readonly name: string;
  readonly sequence: number;
  readonly phase: RoundPhase;
  readonly resultPublished: boolean;
  /** A liga está refazendo a súmula; o placar sai do ar até a republicação. */
  readonly underCorrection: boolean;
  readonly provisional: boolean;
  readonly matches: readonly PublicFixture[];
}

export interface PublicFixtures {
  readonly slug: string;
  readonly name: string;
  readonly timeZoneId: string;
  readonly rounds: readonly PublicRound[];
}

/** O que um atleta fez em campo; só dados públicos (01 §11). */
export interface PublicMatchAthlete {
  readonly sportingName: string;
  readonly position: string;
  readonly playedAsGoalkeeper: boolean;
  readonly goals: number;
  readonly assists: number;
  readonly goalkeeperSaves: number;
  readonly penaltySaves: number;
  readonly yellowCards: number;
  readonly redCards: number;
  readonly ownGoals: number;
  readonly penaltyMisses: number;
  readonly goalsConceded: number;
}

export interface PublicMatchTeam {
  readonly name: string;
  readonly isHome: boolean;
  readonly athletes: readonly PublicMatchAthlete[];
}

export interface PublicMatch {
  readonly id: string;
  readonly slug: string;
  readonly competitionName: string;
  readonly roundName: string;
  readonly stageName: string;
  readonly homeTeamName: string;
  readonly awayTeamName: string;
  readonly homeScore: number;
  readonly awayScore: number;
  readonly kickoffLocal: string;
  readonly provisional: boolean;
  readonly teams: readonly PublicMatchTeam[];
}

/** Calendário e súmulas públicas de um campeonato (Fase 8). */
@Injectable({ providedIn: 'root' })
export class PublicFixtureService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  fixtures(slug: string): Observable<PublicFixtures> {
    return this.http.get<PublicFixtures>(`${this.competitionUrl(slug)}/fixtures`);
  }

  match(slug: string, matchId: string): Observable<PublicMatch> {
    return this.http.get<PublicMatch>(`${this.competitionUrl(slug)}/matches/${matchId}`);
  }

  private competitionUrl(slug: string): string {
    return `${this.baseUrl}/public/competitions/${encodeURIComponent(slug)}`;
  }
}

/** A fase da rodada em pt-BR, do jeito que quem está de fora entende. */
export const ROUND_PHASE_LABELS: Readonly<Record<RoundPhase, string>> = {
  Draft: 'Em montagem',
  MarketOpen: 'Mercado aberto',
  MarketClosed: 'Mercado fechado',
  InProgress: 'Em andamento',
  UnderReview: 'Em conferência',
  Published: 'Resultado provisório',
  Consolidated: 'Resultado final',
  Cancelled: 'Cancelada',
  ReopenedForCorrection: 'Em correção',
};
