import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizationInvitation } from './organization.service';

@Injectable({ providedIn: 'root' })
export class InvitationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Aceita com a conta atual. Repetir com a mesma conta devolve o mesmo convite. */
  accept(token: string): Observable<OrganizationInvitation> {
    return this.http.post<OrganizationInvitation>(
      `${this.baseUrl}/organization-invitations/accept`,
      { token },
    );
  }
}

/**
 * Guarda o token do convite só na memória da aplicação.
 *
 * O link chega com o token na URL, mas ele sai da barra assim que é lido. Quem ainda
 * não entrou precisa passar pelo login antes de aceitar; guardar aqui evita levar o
 * token para a URL do login (`destino`) e evita storage do navegador, que o ADR-004
 * proíbe para tokens de ação. Se a página for recarregada, o token some e a pessoa
 * abre o link do e-mail de novo.
 */
@Injectable({ providedIn: 'root' })
export class PendingInvitation {
  private readonly token = signal<string | null>(null);

  readonly current = this.token.asReadonly();

  keep(token: string): void {
    this.token.set(token);
  }

  clear(): void {
    this.token.set(null);
  }
}
