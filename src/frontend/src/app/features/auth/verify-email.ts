import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Card, Loading } from '../../shared/ui';

/** Destino do link de confirmação enviado por e-mail. */
@Component({
  selector: 'app-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Card, Loading, RouterLink],
  template: `
    <app-card heading="Confirmação de e-mail">
      @switch (estado()) {
        @case ('verificando') {
          <app-loading label="Confirmando seu e-mail…" />
        }
        @case ('confirmado') {
          <app-alert tone="success">{{ mensagem() }}</app-alert>
          <p class="apoio"><a routerLink="/entrar">Entrar na minha conta</a></p>
        }
        @case ('falhou') {
          <app-alert tone="danger">{{ mensagem() }}</app-alert>
          <p class="apoio">
            Peça um link novo criando a conta de novo ou use
            <a routerLink="/recuperar-senha">recuperar senha</a>.
          </p>
        }
      }
    </app-card>
  `,
  styleUrl: './auth.scss',
})
export class VerifyEmailPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly estado = signal<'verificando' | 'confirmado' | 'falhou'>('verificando');
  protected readonly mensagem = signal('');

  constructor() {
    void this.confirmar();
  }

  private async confirmar(): Promise<void> {
    const parametros = this.route.snapshot.queryParamMap;
    const id = parametros.get('id');
    const token = parametros.get('token');

    if (!id || !token) {
      this.estado.set('falhou');
      this.mensagem.set('O link está incompleto. Abra o link do e-mail sem alterá-lo.');
      return;
    }

    try {
      const resposta = await this.auth.confirmEmail(id, token);
      this.mensagem.set(resposta.message);
      this.estado.set('confirmado');
    } catch (falha) {
      this.mensagem.set((falha as ApiFailure).message ?? 'Link inválido ou expirado.');
      this.estado.set('falhou');
    }
  }
}
