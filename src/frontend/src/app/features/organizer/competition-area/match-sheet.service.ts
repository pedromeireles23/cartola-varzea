import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

export type RedCardReason = 'SecondYellow' | 'Direct';

export interface MatchSheetAthlete {
  readonly athleteId: string;
  readonly realTeamId: string;
  readonly sportingName: string;
  readonly position: string;
  readonly didPlay: boolean;
  readonly playedAsGoalkeeper: boolean;
  readonly goalsConceded: number;
  readonly goals: number;
  readonly assists: number;
  readonly goalkeeperSaves: number;
  readonly penaltySaves: number;
  readonly yellowCards: number;
  readonly redCards: number;
  readonly redCardReason: RedCardReason | null;
  readonly ownGoals: number;
  readonly penaltyMisses: number;
}

export interface MatchSheet {
  readonly matchId: string;
  readonly roundId: string;
  readonly roundName: string;
  readonly roundPhase: string;
  readonly kickoffAt: string;
  readonly homeTeamId: string;
  readonly homeTeamName: string;
  readonly awayTeamId: string;
  readonly awayTeamName: string;
  readonly homeScore: number;
  readonly awayScore: number;
  readonly athletes: readonly MatchSheetAthlete[];
  readonly version: string | null;
}

export interface MatchSheetInput {
  readonly homeScore: number;
  readonly awayScore: number;
  readonly appearances: readonly MatchSheetAthlete[];
  readonly version: string | null;
}

@Injectable({ providedIn: 'root' })
export class MatchSheetService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(competitionId: string, matchId: string): Observable<MatchSheet> {
    return this.http.get<MatchSheet>(this.url(competitionId, matchId));
  }

  save(competitionId: string, matchId: string, input: MatchSheetInput): Observable<MatchSheet> {
    return this.http.put<MatchSheet>(this.url(competitionId, matchId), input);
  }

  private url(competitionId: string, matchId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/matches/${encodeURIComponent(matchId)}/sheet`;
  }
}
