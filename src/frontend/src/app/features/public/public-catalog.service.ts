import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

/** Um atleta no elenco do time. `active` é falso para quem foi liberado no meio. */
export interface SquadAthlete {
  readonly id: string;
  readonly sportingName: string;
  readonly position: string;
  readonly price: number;
  readonly active: boolean;
}

export interface PublicTeamDetail {
  readonly id: string;
  readonly slug: string;
  readonly competitionName: string;
  readonly name: string;
  readonly coachName: string | null;
  readonly athletes: readonly SquadAthlete[];
}

/** O que o atleta fez nas rodadas já publicadas. */
export interface AthleteTotals {
  readonly matches: number;
  readonly goals: number;
  readonly assists: number;
  readonly goalkeeperSaves: number;
  readonly penaltySaves: number;
  readonly yellowCards: number;
  readonly redCards: number;
  readonly ownGoals: number;
  readonly penaltyMisses: number;
  readonly cleanSheets: number;
}

export interface AthletePrice {
  readonly roundName: string;
  readonly sequence: number;
  readonly previousPrice: number;
  readonly newPrice: number;
  readonly variation: number;
}

export interface PublicAthlete {
  readonly id: string;
  readonly slug: string;
  readonly competitionName: string;
  readonly sportingName: string;
  readonly position: string;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly price: number;
  readonly active: boolean;
  readonly totals: AthleteTotals;
  readonly priceHistory: readonly AthletePrice[];
}

/** Time e atleta públicos (Fase 8), com só o que o 01 §11 permite de uma pessoa. */
@Injectable({ providedIn: 'root' })
export class PublicCatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  team(slug: string, teamId: string): Observable<PublicTeamDetail> {
    return this.http.get<PublicTeamDetail>(`${this.competitionUrl(slug)}/teams/${teamId}`);
  }

  athlete(slug: string, athleteId: string): Observable<PublicAthlete> {
    return this.http.get<PublicAthlete>(`${this.competitionUrl(slug)}/athletes/${athleteId}`);
  }

  private competitionUrl(slug: string): string {
    return `${this.baseUrl}/public/competitions/${encodeURIComponent(slug)}`;
  }
}

/** Posição em pt-BR; o código vem do domínio em inglês. */
export const POSITION_LABELS: Readonly<Record<string, string>> = {
  Goalkeeper: 'Goleiro',
  Defender: 'Defensor',
  Midfielder: 'Meio-campista',
  Forward: 'Atacante',
};
