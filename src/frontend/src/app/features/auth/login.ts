import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button, Card, FormField } from '../../shared/ui';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, FormField, RouterLink],
  template: `
    <app-card heading="Entrar" [headingLevel]="1">
      <form (submit)="entrar($event)">
        @if (erro()) {
          <app-alert tone="danger">{{ erro() }}</app-alert>
        }

        <app-form-field
          label="E-mail"
          type="email"
          autocomplete="username"
          [required]="true"
          [(value)]="email"
        />

        <app-form-field
          label="Senha"
          type="password"
          autocomplete="current-password"
          [required]="true"
          [(value)]="senha"
        />

        <app-button type="submit" [loading]="enviando()" [fullWidth]="true">Entrar</app-button>
      </form>

      <p class="apoio">
        <a routerLink="/recuperar-senha">Esqueci minha senha</a>
        ·
        <a routerLink="/cadastro">Criar uma conta</a>
      </p>
    </app-card>
  `,
  styleUrl: './auth.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly email = signal('');
  protected readonly senha = signal('');
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected async entrar(event: Event): Promise<void> {
    event.preventDefault();
    this.enviando.set(true);
    this.erro.set(null);

    try {
      await this.auth.login(this.email(), this.senha());

      // O destino volta da rota de origem, para que quem foi barrado numa página
      // privada caia de volta nela e não na home.
      const destino = new URLSearchParams(globalThis.location.search).get('destino');
      await this.router.navigateByUrl(destino ?? '/perfil');
    } catch (falha) {
      // A mensagem é a mesma para e-mail inexistente e senha errada: a API não
      // distingue os dois e a interface não deve inventar essa distinção.
      this.erro.set((falha as ApiFailure).message ?? 'Não foi possível entrar.');
    } finally {
      this.enviando.set(false);
    }
  }
}
