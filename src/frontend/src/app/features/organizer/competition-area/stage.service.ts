import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

export type StageFormat = 'Groups' | 'Knockout';

export type TiebreakCriterion =
  'Wins' | 'GoalDifference' | 'GoalsFor' | 'HeadToHead' | 'FewestRedCards' | 'FewestYellowCards';

export interface StageGroup {
  readonly id: string;
  readonly name: string;
}

export interface Stage {
  readonly id: string;
  readonly name: string;
  readonly format: StageFormat;
  readonly sequence: number;
  readonly groups: readonly StageGroup[];
  readonly tiebreakers: readonly TiebreakCriterion[];
  /** Precisa voltar na edição: o servidor recusa salvar sobre uma leitura antiga. */
  readonly version: string;
}

/** O que o formulário envia; grupo sem `id` é um grupo novo. */
export interface StageInput {
  readonly name: string;
  readonly format: StageFormat;
  readonly groups: readonly { readonly id: string | null; readonly name: string }[];
  readonly tiebreakers: readonly TiebreakCriterion[];
}

/** Espelham as regras do servidor, que continua sendo quem decide. */
export const STAGE_NAME_MIN = 2;
export const STAGE_NAME_MAX = 60;
export const GROUP_NAME_MAX = 30;
export const MAX_GROUPS = 16;
export const MAX_STAGES = 10;

export const FORMAT_LABELS: Readonly<Record<StageFormat, string>> = {
  Groups: 'Fase de grupos',
  Knockout: 'Mata-mata',
};

export const TIEBREAK_LABELS: Readonly<Record<TiebreakCriterion, string>> = {
  Wins: 'Mais vitórias',
  GoalDifference: 'Maior saldo de gols',
  GoalsFor: 'Mais gols marcados',
  HeadToHead: 'Confronto direto',
  FewestRedCards: 'Menos cartões vermelhos',
  FewestYellowCards: 'Menos cartões amarelos',
};

/** A ordem usada pela maioria dos regulamentos no Brasil, igual ao padrão do servidor. */
export const DEFAULT_TIEBREAKERS: readonly TiebreakCriterion[] = [
  'Wins',
  'GoalDifference',
  'GoalsFor',
  'HeadToHead',
  'FewestRedCards',
  'FewestYellowCards',
];

export const STAGE_LIMIT_CODE = 'competition_stage_limit';

@Injectable({ providedIn: 'root' })
export class StageService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(competitionId: string): Observable<Stage[]> {
    return this.http.get<Stage[]>(this.stagesUrl(competitionId));
  }

  create(competitionId: string, stage: StageInput): Observable<Stage> {
    return this.http.post<Stage>(this.stagesUrl(competitionId), stage);
  }

  /** Responde 409 quando a fase mudou depois da leitura que gerou `version`. */
  update(
    competitionId: string,
    stageId: string,
    stage: StageInput,
    version: string,
  ): Observable<Stage> {
    return this.http.put<Stage>(`${this.stagesUrl(competitionId)}/${encodeURIComponent(stageId)}`, {
      ...stage,
      version,
    });
  }

  remove(competitionId: string, stageId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.stagesUrl(competitionId)}/${encodeURIComponent(stageId)}`,
    );
  }

  /** A lista precisa ter exatamente as fases atuais; senão o servidor responde 409. */
  reorder(competitionId: string, stageIds: readonly string[]): Observable<Stage[]> {
    return this.http.put<Stage[]>(`${this.stagesUrl(competitionId)}/order`, { stageIds });
  }

  private stagesUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/stages`;
  }
}
