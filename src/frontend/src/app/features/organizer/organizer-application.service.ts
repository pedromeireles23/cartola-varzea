import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

export type OrganizerApplicationStatus = 'Pending' | 'Approved' | 'Rejected';

/** Solicitação de organizador, como a API devolve. */
export interface OrganizerApplication {
  readonly id: string;
  readonly organizationName: string;
  readonly status: OrganizerApplicationStatus;
  readonly submittedAt: string;
  readonly decidedAt: string | null;
  readonly decisionReason: string | null;
  readonly organizationId: string | null;
}

/** Limites do nome da organização, iguais aos da API. */
export const ORGANIZATION_NAME_MIN = 3;
export const ORGANIZATION_NAME_MAX = 120;

@Injectable({ providedIn: 'root' })
export class OrganizerApplicationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Solicitações da conta atual, da mais recente para a mais antiga. */
  mine(): Observable<OrganizerApplication[]> {
    return this.http.get<OrganizerApplication[]>(`${this.baseUrl}/organizer-applications/mine`);
  }

  /**
   * Envia a solicitação. Se já existir uma em análise, a API devolve essa mesma
   * em vez de criar outra, então repetir o envio é seguro.
   */
  submit(organizationName: string): Observable<OrganizerApplication> {
    return this.http.post<OrganizerApplication>(`${this.baseUrl}/organizer-applications`, {
      organizationName,
    });
  }
}
