import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

export type AthletePosition = 'Goalkeeper' | 'Defender' | 'Midfielder' | 'Forward';
export type PriceTier = 'Basic' | 'Regular' | 'Star';
export type RosterStatus = 'Active' | 'Released';

export interface Athlete {
  readonly id: string;
  readonly sportingName: string;
  readonly position: AthletePosition;
  readonly realTeamId: string;
  readonly realTeamName: string;
  readonly priceTier: PriceTier;
  readonly initialPriceOverride: number | null;
  readonly initialPrice: number;
  readonly isAvailable: boolean;
  readonly status: RosterStatus;
  readonly updatedAt: string;
  readonly version: string;
}

export interface AthleteInput {
  readonly sportingName: string;
  readonly position: AthletePosition;
  readonly realTeamId: string;
  readonly priceTier: PriceTier;
  readonly initialPriceOverride: number | null;
}

export const ATHLETE_NAME_MIN = 2;
export const ATHLETE_NAME_MAX = 80;
export const ATHLETE_MIN_PRICE = 1;
export const ATHLETE_MAX_PRICE = 30;
export const ATHLETE_DUPLICATE_CODE = 'athlete_sporting_name_duplicate';
export const ATHLETE_TRANSFER_CODE = 'athlete_transfer_not_allowed';
export const ATHLETE_POSITION_LOCKED_CODE = 'athlete_position_locked';

@Injectable({ providedIn: 'root' })
export class AthleteService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(competitionId: string): Observable<Athlete[]> {
    return this.http.get<Athlete[]>(this.athletesUrl(competitionId));
  }

  create(competitionId: string, input: AthleteInput): Observable<Athlete> {
    return this.http.post<Athlete>(this.athletesUrl(competitionId), input);
  }

  update(
    competitionId: string,
    athleteId: string,
    input: AthleteInput,
    version: string,
  ): Observable<Athlete> {
    return this.http.put<Athlete>(
      `${this.athletesUrl(competitionId)}/${encodeURIComponent(athleteId)}`,
      { ...input, version },
    );
  }

  release(competitionId: string, athleteId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.athletesUrl(competitionId)}/${encodeURIComponent(athleteId)}`,
    );
  }

  private athletesUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/athletes`;
  }
}
