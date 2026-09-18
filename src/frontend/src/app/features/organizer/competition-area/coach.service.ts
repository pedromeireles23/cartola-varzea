import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';
import { PriceTier } from './athlete.service';

export interface Coach {
  readonly id: string;
  readonly displayName: string | null;
  readonly effectiveName: string;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly priceTier: PriceTier;
  readonly initialPriceOverride: number | null;
  readonly initialPrice: number;
  readonly isAvailable: boolean;
  /** O time ficou de fora da fase mais adiantada já confirmada. */
  readonly isEliminated: boolean;
  readonly updatedAt: string;
  readonly version: string;
}

export interface CoachInput {
  readonly displayName: string | null;
  readonly priceTier: PriceTier;
  readonly initialPriceOverride: number | null;
}

export const COACH_NAME_MIN = 2;
export const COACH_NAME_MAX = 80;
export const COACH_MIN_PRICE = 1;
export const COACH_MAX_PRICE = 30;

@Injectable({ providedIn: 'root' })
export class CoachService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(competitionId: string): Observable<Coach[]> {
    return this.http.get<Coach[]>(this.coachesUrl(competitionId));
  }

  update(
    competitionId: string,
    coachId: string,
    input: CoachInput,
    version: string,
  ): Observable<Coach> {
    return this.http.put<Coach>(
      `${this.coachesUrl(competitionId)}/${encodeURIComponent(coachId)}`,
      { ...input, version },
    );
  }

  private coachesUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/coaches`;
  }
}
