import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';

/** Conta autenticada, como a API devolve em GET /auth/me. */
export interface Account {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailConfirmed: boolean;
  readonly roles: readonly string[];
}

/** Resposta dos endpoints que respondem igual exista ou não a conta. */
export interface MessageResponse {
  readonly message: string;
}

/**
 * Estado da sessão.
 *
 * Nada de token no browser: a sessão vive num cookie HttpOnly que o script não
 * enxerga. O que existe aqui é só a resposta de /auth/me, usada para decidir qual
 * casca montar — a autorização de verdade é sempre do servidor.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly account = signal<Account | null>(null);
  private readonly loaded = signal(false);

  /** Conta atual, ou `null` quando anônima. */
  readonly current = this.account.asReadonly();

  /** Verdadeiro depois da primeira consulta a /auth/me. */
  readonly ready = this.loaded.asReadonly();

  readonly isAuthenticated = computed(() => this.account() !== null);

  /** Só decide o que mostrar; a API continua sendo quem autoriza. */
  /** Conta pública de demonstração: tudo é somente leitura no servidor. */
  readonly isDemoViewer = computed(() => this.account()?.roles.includes('DemoViewer') ?? false);

  readonly isPlatformAdmin = computed(
    () => this.account()?.roles.includes('PlatformAdmin') ?? false,
  );

  /**
   * Busca o par de tokens antiforgery.
   *
   * Precisa ser refeito a cada mudança de sessão: o token é vinculado à identidade
   * autenticada, então o emitido para o anônimo deixa de valer depois da entrada, e
   * vice-versa. Esquecer isso faz a primeira ação depois do login falhar com 400.
   */
  async refreshAntiforgery(): Promise<void> {
    await firstValueFrom(
      this.http.get(`${this.baseUrl}/auth/antiforgery`, { observe: 'response' }),
    );
  }

  /** Carrega o estado da sessão. Chamado na inicialização da aplicação. */
  async load(): Promise<void> {
    await this.refreshAntiforgery();

    try {
      // 204 chega como corpo nulo: é assim que a API diz "anônimo" sem ser um erro.
      const conta = await firstValueFrom(this.http.get<Account | null>(`${this.baseUrl}/auth/me`));
      this.account.set(conta ?? null);
    } catch {
      this.account.set(null);
    } finally {
      this.loaded.set(true);
    }
  }

  async register(email: string, displayName: string, password: string): Promise<MessageResponse> {
    return firstValueFrom(
      this.http.post<MessageResponse>(`${this.baseUrl}/auth/register`, {
        email,
        displayName,
        password,
      }),
    );
  }

  async confirmEmail(userId: string, token: string): Promise<MessageResponse> {
    return firstValueFrom(
      this.http.post<MessageResponse>(`${this.baseUrl}/auth/confirm-email`, { userId, token }),
    );
  }

  async login(email: string, password: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.baseUrl}/auth/login`, { email, password }, { observe: 'response' }),
    );

    await this.load();
  }

  async logout(): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.baseUrl}/auth/logout`, null, { observe: 'response' }),
    );

    this.account.set(null);
    await this.refreshAntiforgery();
  }

  async forgotPassword(email: string): Promise<MessageResponse> {
    return firstValueFrom(
      this.http.post<MessageResponse>(`${this.baseUrl}/auth/forgot-password`, { email }),
    );
  }

  async resetPassword(
    userId: string,
    token: string,
    newPassword: string,
  ): Promise<MessageResponse> {
    return firstValueFrom(
      this.http.post<MessageResponse>(`${this.baseUrl}/auth/reset-password`, {
        userId,
        token,
        newPassword,
      }),
    );
  }
}
