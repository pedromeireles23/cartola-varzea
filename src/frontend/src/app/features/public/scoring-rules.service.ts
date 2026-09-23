import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { Modality, ModalityProfile } from '../organizer/competition.service';

export interface GoalPoints {
  readonly position: string;
  readonly points: number;
}

/** Uma faixa de valorização: o limite da diferença e quanto o preço anda nela. */
export interface ValuationBand {
  readonly threshold: number;
  readonly variation: number;
}

export interface Valuation {
  readonly falls: readonly ValuationBand[];
  readonly rises: readonly ValuationBand[];
  readonly floor: number;
  readonly ceiling: number;
}

/**
 * A regra de uma modalidade. Gol, assistência, jogo sem sofrer gol e gol sofrido mudam
 * com a modalidade, porque dependem de quantos gols ela produz; o resto vale o mesmo.
 */
export interface ModalityRules {
  readonly modality: Modality;
  readonly version: number;
  readonly profile: ModalityProfile;
  readonly goalPoints: readonly GoalPoints[];
  readonly assist: number;
  readonly cleanSheet: number;
  readonly goalConceded: number;
  readonly goalkeeperSave: number;
  readonly penaltySave: number;
  readonly yellowCard: number;
  readonly redCard: number;
  readonly ownGoal: number;
  readonly penaltyMiss: number;
  readonly captainMultiplier: number;
  readonly valuation: Valuation;
}

export interface ScoringRules {
  readonly modalities: readonly ModalityRules[];
}

/** Regras de pontuação e valorização (01 §9), iguais para todo campeonato da modalidade. */
@Injectable({ providedIn: 'root' })
export class ScoringRulesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(): Observable<ScoringRules> {
    return this.http.get<ScoringRules>(`${this.baseUrl}/public/scoring-rules`);
  }
}
