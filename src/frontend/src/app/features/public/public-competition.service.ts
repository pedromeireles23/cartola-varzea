import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { Modality, ModalityProfile } from '../organizer/competition.service';
import { StageFormat } from '../organizer/competition-area/stage.service';

/** Linha da busca pública. O endereço do campeonato aqui fora é o slug, não o GUID. */
export interface PublicCompetitionSummary {
  readonly slug: string;
  readonly name: string;
  readonly season: string;
  readonly modality: Modality;
  readonly organizationName: string;
  readonly publishedAt: string;
}

export interface PublicTeam {
  /** Identificador do time; é por ele que a página do elenco é aberta. */
  readonly id: string;
  readonly name: string;
  readonly athletes: number;
}

export interface PublicStageGroup {
  readonly name: string;
  readonly teams: readonly string[];
}

export interface PublicStage {
  readonly name: string;
  readonly format: StageFormat;
  readonly sequence: number;
  readonly groups: readonly PublicStageGroup[];
  /** Times sem grupo: é o caso do mata-mata. */
  readonly teams: readonly string[];
}

export interface PublicCompetition {
  readonly slug: string;
  readonly name: string;
  readonly season: string;
  readonly modality: Modality;
  readonly organizationName: string;
  readonly timeZoneId: string;
  readonly publishedAt: string;
  readonly modalityProfile: ModalityProfile;
  readonly stages: readonly PublicStage[];
  readonly teams: readonly PublicTeam[];
}

/**
 * Uma linha da classificação. `tied` marca quem divide a colocação e `lastRoundPoints`
 * é nulo para quem não jogou a última rodada apurada.
 */
export interface RankingRow {
  readonly position: number;
  readonly tied: boolean;
  readonly displayName: string;
  readonly totalPoints: number;
  readonly netWorth: number;
  readonly lastRoundPoints: number | null;
  readonly isViewer: boolean;
}

/** O ranking geral acumulado do campeonato (01 §9), público como o resto da página. */
export interface Ranking {
  readonly competitionName: string;
  readonly rounds: number;
  readonly lastRoundName: string | null;
  readonly provisional: boolean;
  readonly entries: readonly RankingRow[];
}

@Injectable({ providedIn: 'root' })
export class PublicCompetitionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Busca vazia devolve os campeonatos publicados mais recentes. */
  search(query: string): Observable<PublicCompetitionSummary[]> {
    const termo = query.trim();
    return this.http.get<PublicCompetitionSummary[]>(this.competitionsUrl, {
      params: termo ? new HttpParams().set('busca', termo) : undefined,
    });
  }

  get(slug: string): Observable<PublicCompetition> {
    return this.http.get<PublicCompetition>(`${this.competitionsUrl}/${encodeURIComponent(slug)}`);
  }

  ranking(slug: string): Observable<Ranking> {
    return this.http.get<Ranking>(`${this.competitionsUrl}/${encodeURIComponent(slug)}/ranking`);
  }

  private get competitionsUrl(): string {
    return `${this.baseUrl}/public/competitions`;
  }
}
