import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/config/api-base-url';

/** Estado guardado da rodada; o que a tela mostra é a fase. */
export type RoundStatus = 'Draft' | 'MarketOpen' | 'UnderReview' | 'Published' | 'Cancelled';

/** Ciclo completo, incluindo o que o relógio alcança sozinho (01 §8). */
export type RoundPhase =
  | 'Draft'
  | 'MarketOpen'
  | 'MarketClosed'
  | 'InProgress'
  | 'UnderReview'
  | 'Published'
  | 'Consolidated'
  | 'ReopenedForCorrection'
  | 'Cancelled';

export type MatchStatus = 'Scheduled' | 'Postponed' | 'Cancelled';

export type RoundTransition = 'OpenMarket' | 'ReopenForEditing' | 'SendToReview' | 'Cancel';

export interface Match {
  readonly id: string;
  readonly stageId: string;
  readonly stageName: string;
  readonly homeTeamId: string;
  readonly homeTeamName: string;
  readonly awayTeamId: string;
  readonly awayTeamName: string;
  readonly groupName: string | null;
  readonly kickoffAt: string;
  /** Mesmo instante no fuso do campeonato, pronto para o campo de data e hora. */
  readonly kickoffLocal: string;
  readonly status: MatchStatus;
}

export interface Round {
  readonly id: string;
  readonly name: string;
  readonly sequence: number;
  readonly status: RoundStatus;
  readonly phase: RoundPhase;
  readonly marketCloseAt: string | null;
  readonly marketCloseLocal: string | null;
  readonly matches: readonly Match[];
  /** Precisa voltar na edição: o servidor recusa salvar sobre uma leitura antiga. */
  readonly version: string;
}

export interface RoundReviewEvent {
  readonly type: string;
  readonly quantity: number;
}

export interface RoundReviewMatch {
  readonly matchId: string;
  readonly homeTeamName: string;
  readonly awayTeamName: string;
  readonly kickoffLocal: string;
  readonly status: MatchStatus;
  readonly requiresSheet: boolean;
  readonly hasSheet: boolean;
  readonly homeScore: number | null;
  readonly awayScore: number | null;
  readonly participants: number;
  readonly events: readonly RoundReviewEvent[];
}

export interface RoundReviewPending {
  readonly code: string;
  readonly message: string;
  readonly matchId: string | null;
}

/**
 * A apuração vigente de uma rodada publicada. Até `consolidatesAt` o resultado é
 * provisório; os horários também vêm no fuso do campeonato.
 */
export interface RoundPublication {
  readonly revision: number;
  readonly scoringRuleSetVersion: number;
  readonly publishedAt: string;
  readonly publishedAtLocal: string;
  readonly consolidatesAt: string;
  readonly consolidatesAtLocal: string;
  readonly consolidated: boolean;
  readonly entries: number;
  readonly highestTotal: number | null;
  readonly averageTotal: number | null;
  /** Por que esta revisão existe; nula na primeira publicação. */
  readonly correctionReason: string | null;
}

/**
 * A reabertura em andamento. Enquanto ela existe, `publication` continua descrevendo a
 * apuração que vale para quem joga: a correção só troca os números na republicação.
 */
export interface RoundCorrection {
  readonly reopenedAt: string;
  readonly reopenedAtLocal: string;
  readonly reason: string | null;
}

export interface RoundReview {
  readonly roundId: string;
  readonly roundName: string;
  readonly phase: RoundPhase;
  readonly scheduledMatches: number;
  readonly completedSheets: number;
  readonly ready: boolean;
  readonly matches: readonly RoundReviewMatch[];
  readonly pending: readonly RoundReviewPending[];
  readonly version: string;
  /** Nula enquanto a rodada não foi publicada. */
  readonly publication: RoundPublication | null;
  /** Nula quando a rodada não está reaberta para correção. */
  readonly correction: RoundCorrection | null;
}

/** O que o formulário de partida envia. */
export interface MatchInput {
  readonly stageId: string;
  readonly homeTeamId: string;
  readonly awayTeamId: string;
  readonly kickoffLocal: string;
  readonly status?: MatchStatus;
}

/** Espelham as regras do servidor, que continua sendo quem decide. */
export const ROUND_NAME_MIN = 2;
export const ROUND_NAME_MAX = 60;
export const MAX_ROUNDS = 60;

/** Código estável de quando o estado da rodada não aceita a operação. */
export const ROUND_STATUS_CODE = 'competition_round_status';

/** O servidor recusa um motivo que não explica nada; a tela avisa antes de enviar. */
export const CORRECTION_REASON_MIN = 10;
export const CORRECTION_REASON_MAX = 300;

export const PHASE_LABELS: Readonly<Record<RoundPhase, string>> = {
  Draft: 'Rascunho',
  MarketOpen: 'Mercado aberto',
  MarketClosed: 'Mercado fechado',
  InProgress: 'Em andamento',
  // O mesmo nome que a página pública usa: aqui a organização confere as súmulas; a
  // apuração só acontece na publicação.
  UnderReview: 'Em conferência',
  Published: 'Publicada',
  Consolidated: 'Consolidada',
  ReopenedForCorrection: 'Em correção',
  Cancelled: 'Cancelada',
};

export const MATCH_STATUS_LABELS: Readonly<Record<MatchStatus, string>> = {
  Scheduled: 'Marcada',
  Postponed: 'Adiada',
  Cancelled: 'Cancelada',
};

@Injectable({ providedIn: 'root' })
export class RoundService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(competitionId: string): Observable<Round[]> {
    return this.http.get<Round[]>(this.roundsUrl(competitionId));
  }

  review(competitionId: string, roundId: string): Observable<RoundReview> {
    return this.http.get<RoundReview>(`${this.roundUrl(competitionId, roundId)}/review`);
  }

  create(competitionId: string, name: string): Observable<Round> {
    return this.http.post<Round>(this.roundsUrl(competitionId), { name });
  }

  rename(competitionId: string, roundId: string, name: string, version: string): Observable<Round> {
    return this.http.put<Round>(this.roundUrl(competitionId, roundId), { name, version });
  }

  remove(competitionId: string, roundId: string): Observable<void> {
    return this.http.delete<void>(this.roundUrl(competitionId, roundId));
  }

  /**
   * Apura e publica a rodada em revisão. Devolve a conferência já com o resumo da
   * apuração; repetir o pedido não apura de novo.
   */
  publish(competitionId: string, roundId: string, version: string): Observable<RoundReview> {
    return this.http.post<RoundReview>(`${this.roundUrl(competitionId, roundId)}/publish`, {
      version,
    });
  }

  /**
   * Reabre a rodada publicada para corrigir a súmula. Nada é recalculado agora: a
   * apuração vigente continua valendo até o organizador republicar.
   */
  reopen(
    competitionId: string,
    roundId: string,
    version: string,
    reason: string | null,
  ): Observable<RoundReview> {
    return this.http.post<RoundReview>(`${this.roundUrl(competitionId, roundId)}/reopen`, {
      version,
      reason,
    });
  }

  changeStatus(
    competitionId: string,
    roundId: string,
    transition: RoundTransition,
    version: string,
  ): Observable<Round> {
    return this.http.put<Round>(`${this.roundUrl(competitionId, roundId)}/status`, {
      transition,
      version,
    });
  }

  addMatch(
    competitionId: string,
    roundId: string,
    match: MatchInput,
    version: string,
  ): Observable<Round> {
    return this.http.post<Round>(`${this.roundUrl(competitionId, roundId)}/matches`, {
      ...match,
      version,
    });
  }

  updateMatch(
    competitionId: string,
    roundId: string,
    matchId: string,
    match: MatchInput,
    version: string,
  ): Observable<Round> {
    return this.http.put<Round>(
      `${this.roundUrl(competitionId, roundId)}/matches/${encodeURIComponent(matchId)}`,
      { ...match, version },
    );
  }

  removeMatch(
    competitionId: string,
    roundId: string,
    matchId: string,
    version: string,
  ): Observable<Round> {
    return this.http.delete<Round>(
      `${this.roundUrl(competitionId, roundId)}/matches/${encodeURIComponent(matchId)}`,
      { params: new HttpParams().set('version', version) },
    );
  }

  private roundsUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}/rounds`;
  }

  private roundUrl(competitionId: string, roundId: string): string {
    return `${this.roundsUrl(competitionId)}/${encodeURIComponent(roundId)}`;
  }
}
