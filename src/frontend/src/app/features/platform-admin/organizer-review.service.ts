import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizerApplication } from '../organizer/organizer-application.service';

/** Item da fila de análise, com quem pediu. Só o Platform admin recebe. */
export interface PendingOrganizerApplication {
  readonly id: string;
  readonly organizationName: string;
  readonly submittedAt: string;
  readonly applicantUserId: string;
  readonly applicantDisplayName: string;
  readonly applicantEmail: string;
}

export type ReviewDecision = 'approve' | 'reject';

/** Limites do motivo, iguais aos da API. */
export const REVIEW_REASON_MIN = 3;
export const REVIEW_REASON_MAX = 500;

@Injectable({ providedIn: 'root' })
export class OrganizerReviewService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  pending(): Observable<PendingOrganizerApplication[]> {
    return this.http.get<PendingOrganizerApplication[]>(
      `${this.baseUrl}/platform-admin/organizer-applications/pending`,
    );
  }

  /**
   * Aprova ou rejeita. Repetir a mesma decisão devolve o mesmo resultado; uma decisão
   * diferente da já registrada responde 409.
   */
  decide(
    applicationId: string,
    decision: ReviewDecision,
    reason: string,
  ): Observable<OrganizerApplication> {
    return this.http.post<OrganizerApplication>(
      `${this.baseUrl}/platform-admin/organizer-applications/${applicationId}/${decision}`,
      { reason },
    );
  }
}
