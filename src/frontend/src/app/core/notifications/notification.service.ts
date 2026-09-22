import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

import { API_BASE_URL } from '../config/api-base-url';

export type NotificationKind = 'RoundPublished' | 'RoundCorrected';

/**
 * Um aviso pronto para a tela: o servidor já monta o texto a partir do tipo e dos nomes
 * atuais. `roundId` não nulo significa que o aviso leva à pontuação daquela rodada.
 */
export interface NotificationItem {
  readonly id: string;
  readonly kind: NotificationKind;
  readonly title: string;
  readonly body: string;
  readonly competitionSlug: string;
  readonly roundId: string | null;
  readonly createdAt: string;
  readonly read: boolean;
}

export interface NotificationInbox {
  readonly unread: number;
  readonly items: readonly NotificationItem[];
}

const VAZIA: NotificationInbox = { unread: 0, items: [] };

/**
 * A caixa de avisos da conta (01 §14). Ela vive num signal só, compartilhado, porque o
 * sino aparece em todas as telas autenticadas.
 *
 * Toda falha é engolida de propósito: o aviso é acessório e não pode derrubar a página
 * que a pessoa veio ver (04 §17). Sem sessão, a API responde 401 e a caixa fica vazia.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly caixa = signal<NotificationInbox>(VAZIA);

  readonly inbox = this.caixa.asReadonly();
  readonly naoLidas = computed(() => this.caixa().unread);

  carregar(): void {
    this.http.get<NotificationInbox>(`${this.baseUrl}/notifications`).subscribe({
      next: (caixa) => this.caixa.set(caixa),
      error: () => this.caixa.set(VAZIA),
    });
  }

  /** Abrir a caixa é ler: o servidor devolve a lista já marcada. */
  marcarLidas(): void {
    if (this.caixa().unread === 0) return;

    this.http.post<NotificationInbox>(`${this.baseUrl}/notifications/read`, {}).subscribe({
      next: (caixa) => this.caixa.set(caixa),
      error: () => undefined,
    });
  }

  limpar(): void {
    this.caixa.set(VAZIA);
  }
}
