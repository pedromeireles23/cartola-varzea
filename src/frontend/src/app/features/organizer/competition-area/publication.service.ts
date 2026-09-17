import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { COMPETITION_NOT_READY } from '../../../core/api/problem-details';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionStatus } from '../competition.service';

export type ReadinessSeverity = 'Blocker' | 'Warning';

export interface ReadinessItem {
  /** Código estável da regra; o texto de apoio da tela é escolhido por ele. */
  readonly code: string;
  readonly severity: ReadinessSeverity;
  readonly message: string;
}

export interface CompetitionReadiness {
  readonly competitionId: string;
  readonly status: CompetitionStatus;
  readonly publishedAt: string | null;
  /** Endereço público, dado na publicação; nulo enquanto o campeonato nunca foi publicado. */
  readonly slug: string | null;
  readonly canPublish: boolean;
  readonly items: readonly ReadinessItem[];
  /** Precisa voltar na publicação: o servidor recusa decidir sobre uma leitura antiga. */
  readonly version: string;
}

/** Código do Problem Details quando o checklist ainda tem impedimentos. */
export const NOT_READY_CODE = COMPETITION_NOT_READY;

/** Onde o organizador resolve cada impedimento, na navegação da área. */
export const READINESS_LINKS: Readonly<
  Record<string, { readonly rota: string; readonly texto: string }>
> = {
  no_stages: { rota: 'fases', texto: 'Criar fase' },
  no_stage_participants: { rota: 'fases', texto: 'Confirmar times da fase' },
  stage_without_participants: { rota: 'fases', texto: 'Confirmar times da fase' },
  not_enough_teams: { rota: 'times', texto: 'Cadastrar times' },
  not_enough_athletes: { rota: 'atletas', texto: 'Cadastrar atletas' },
  thin_real_team_roster: { rota: 'atletas', texto: 'Cadastrar atletas' },
  single_price_level: { rota: 'atletas', texto: 'Revisar níveis de preço' },
};

@Injectable({ providedIn: 'root' })
export class PublicationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  readiness(competitionId: string): Observable<CompetitionReadiness> {
    return this.http.get<CompetitionReadiness>(`${this.competitionUrl(competitionId)}/readiness`);
  }

  /**
   * Publica ou volta para rascunho. Responde 409 com `competition_not_ready` quando o
   * checklist tem impedimentos, e 409 simples quando alguém decidiu antes.
   */
  setPublished(
    competitionId: string,
    published: boolean,
    version: string,
  ): Observable<CompetitionReadiness> {
    return this.http.put<CompetitionReadiness>(
      `${this.competitionUrl(competitionId)}/publication`,
      {
        published,
        version,
      },
    );
  }

  private competitionUrl(competitionId: string): string {
    return `${this.baseUrl}/competitions/${encodeURIComponent(competitionId)}`;
  }
}
