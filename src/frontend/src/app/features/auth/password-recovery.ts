import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button, Card, FormField } from '../../shared/ui';

/**
 * Uma tela só para as duas metades da recuperação.
 *
 * Sem token na URL, pede o e-mail; com token, pede a senha nova. É o mesmo assunto
 * para quem usa, e separar em duas rotas só criaria um caminho a mais para errar.
 */
@Component({
  selector: 'app-password-recovery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, FormField, RouterLink],
  template: `
    <app-card [heading]="temToken() ? 'Criar senha nova' : 'Recuperar senha'">
      @if (mensagem()) {
        <app-alert tone="success">{{ mensagem() }}</app-alert>
        @if (temToken()) {
          <p class="apoio"><a routerLink="/entrar">Entrar com a senha nova</a></p>
        }
      } @else {
        <form (submit)="enviar($event)">
          @if (erro()) {
            <app-alert tone="danger">{{ erro() }}</app-alert>
          }

          @if (temToken()) {
            <app-form-field
              label="Senha nova"
              type="password"
              autocomplete="new-password"
              hint="Pelo menos 12 caracteres."
              [minLength]="12"
              [required]="true"
              [(value)]="senha"
            />
          } @else {
            <p class="apoio">
              Informe o e-mail da sua conta. Se ele puder ser usado, enviamos um link.
            </p>
            <app-form-field
              label="E-mail"
              type="email"
              autocomplete="username"
              [required]="true"
              [(value)]="email"
            />
          }

          <app-button type="submit" [loading]="enviando()" [fullWidth]="true">
            {{ temToken() ? 'Salvar senha nova' : 'Enviar link' }}
          </app-button>
        </form>
      }
    </app-card>
  `,
  styleUrl: './auth.scss',
})
export class PasswordRecoveryPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly parametros = this.route.snapshot.queryParamMap;

  protected readonly email = signal('');
  protected readonly senha = signal('');
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly mensagem = signal<string | null>(null);

  protected readonly temToken = signal(this.parametros.has('id') && this.parametros.has('token'));

  protected async enviar(event: Event): Promise<void> {
    event.preventDefault();
    this.enviando.set(true);
    this.erro.set(null);

    try {
      const resposta = this.temToken()
        ? await this.auth.resetPassword(
            this.parametros.get('id') ?? '',
            this.parametros.get('token') ?? '',
            this.senha(),
          )
        : await this.auth.forgotPassword(this.email());

      this.mensagem.set(resposta.message);

      // Tira o token da barra de endereços assim que ele é usado, para que não
      // fique no histórico do navegador nem seja compartilhado por engano.
      if (this.temToken()) {
        await this.router.navigate([], { queryParams: {}, replaceUrl: true });
      }
    } catch (falha) {
      this.erro.set((falha as ApiFailure).message ?? 'Não foi possível concluir.');
    } finally {
      this.enviando.set(false);
    }
  }
}
