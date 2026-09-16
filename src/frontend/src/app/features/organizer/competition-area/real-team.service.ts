import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

export interface RealTeam {
  readonly id: string;
  readonly name: string;
  readonly isArchived: boolean;
  readonly updatedAt: string;
  /** Volta na edição para impedir que uma leitura antiga sobrescreva outra alteração. */
  readonly version: string;
}

export const REAL_TEAM_NAME_MIN = 2;
export const REAL_TEAM_NAME_MAX = 80;
export const REAL_TEAM_DUPLICATE_CODE = 'real_team_name_duplicate';

@Injectable({ providedIn: 'root' })
export class RealTeamService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(competitionId: string): Observable<RealTeam[]> {
    return this.http.get<RealTeam[]>(this.teamsUrl(competitionId));
  }

  create(competitionId: string, name: string): Observable<RealTeam> {
    return this.http.post<RealTeam>(this.teamsUrl(competitionId), { name });
  }

  update(
    competitionId: string,
    teamId: string,
    name: string,
    version: string,
  ): Observable<RealTeam> {
    return this.http.put<RealTeam>(
      `${this.teamsUrl(competitionId)}/${encodeURIComponent(teamId)}`,
      { name, version },
    );
  }

  archive(competitionId: string, teamId: string): Observable<void> {
    return this.http.delete<void>(`${this.teamsUrl(competitionId)}/${encodeURIComponent(teamId)}`);
  }

  private teamsUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/teams`;
  }
}
