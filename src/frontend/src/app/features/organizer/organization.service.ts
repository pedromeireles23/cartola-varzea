import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';

export type OrganizationRole = 'Owner' | 'Assistant';

/** Organização da conta atual, com o papel dela. */
export interface MyOrganization {
  readonly id: string;
  readonly name: string;
  readonly role: OrganizationRole;
  readonly joinedAt: string;
}

export type InvitationStatus = 'Pending' | 'Accepted' | 'Revoked' | 'Expired';

export interface OrganizationInvitation {
  readonly id: string;
  readonly organizationId: string;
  readonly invitedEmail: string;
  readonly status: InvitationStatus;
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly acceptedAt: string | null;
  readonly revokedAt: string | null;
}

export interface OrganizationAssistant {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly joinedAt: string;
}

/** Equipe da organização. Só o proprietário recebe. */
export interface OrganizationTeam {
  readonly organizationId: string;
  readonly organizationName: string;
  readonly assistants: readonly OrganizationAssistant[];
  readonly invitations: readonly OrganizationInvitation[];
}

@Injectable({ providedIn: 'root' })
export class OrganizationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  mine(): Observable<MyOrganization[]> {
    return this.http.get<MyOrganization[]>(`${this.baseUrl}/organizations/mine`);
  }

  /** Uma organização da conta, com o papel nela. Responde 403 para quem não é membro. */
  get(organizationId: string): Observable<MyOrganization> {
    return this.http.get<MyOrganization>(
      `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}`,
    );
  }

  team(organizationId: string): Observable<OrganizationTeam> {
    return this.http.get<OrganizationTeam>(
      `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}/team`,
    );
  }

  /** Convidar de novo o mesmo e-mail revoga o convite pendente anterior. */
  invite(organizationId: string, email: string): Observable<OrganizationInvitation> {
    return this.http.post<OrganizationInvitation>(
      `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}/team/invitations`,
      { email },
    );
  }

  /** Retira um auxiliar. A API responde 404 se ele já não fizer parte da equipe. */
  removeAssistant(organizationId: string, userId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}/team/assistants/${encodeURIComponent(userId)}`,
    );
  }

  revoke(organizationId: string, invitationId: string): Observable<OrganizationInvitation> {
    return this.http.delete<OrganizationInvitation>(
      `${this.baseUrl}/organizations/${encodeURIComponent(organizationId)}/team/invitations/${encodeURIComponent(invitationId)}`,
    );
  }
}
