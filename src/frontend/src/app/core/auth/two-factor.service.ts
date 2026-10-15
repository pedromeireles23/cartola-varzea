import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import { AuthService } from './auth.service';

/** Estado dos dois fatores da conta, como a API devolve em GET /auth/two-factor. */
export interface TwoFactorStatus {
  readonly enabled: boolean;
  readonly recoveryCodesLeft: number;
}

/** O que a pessoa leva para o aplicativo autenticador. */
export interface AuthenticatorSetup {
  /** A chave em grupos de quatro, para digitar. */
  readonly sharedKey: string;
  /** O endereço `otpauth://`, conteúdo do QR code. */
  readonly authenticatorUri: string;
}

/**
 * Verificação em duas etapas por aplicativo autenticador (Fase 20). Obrigatória para a
 * administração da plataforma: a API só abre `/platform-admin` para sessão que passou
 * pelo código.
 */
@Injectable({ providedIn: 'root' })
export class TwoFactorService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly auth = inject(AuthService);

  status(): Promise<TwoFactorStatus> {
    return firstValueFrom(this.http.get<TwoFactorStatus>(`${this.baseUrl}/auth/two-factor`));
  }

  /** Gera uma chave nova; a anterior, se havia, deixa de valer. */
  beginAuthenticator(): Promise<AuthenticatorSetup> {
    return firstValueFrom(
      this.http.post<AuthenticatorSetup>(`${this.baseUrl}/auth/two-factor/authenticator`, null),
    );
  }

  /**
   * Liga os dois fatores e devolve os códigos de recuperação — a única vez que eles saem.
   * A sessão continua, agora de dois fatores; a conta é relida para a interface saber.
   */
  async enable(code: string): Promise<readonly string[]> {
    const resposta = await firstValueFrom(
      this.http.post<{ recoveryCodes: string[] }>(`${this.baseUrl}/auth/two-factor/enable`, {
        code,
      }),
    );

    await this.auth.load();
    return resposta.recoveryCodes;
  }
}
