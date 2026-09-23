import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Alert, Button, Card, FormField } from '../../shared/ui';

@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, FormField, RouterLink],
  template: `
    <app-card heading="Criar conta" [headingLevel]="1">
      @if (enviado()) {
        <app-alert tone="success">{{ enviado() }}</app-alert>
        <p class="apoio">Confira sua caixa de entrada e clique no link de confirmação.</p>
      } @else {
        <form (submit)="cadastrar($event)">
          @if (erro()) {
            <app-alert tone="danger">{{ erro() }}</app-alert>
          }

          <app-form-field
            label="Como quer ser chamado"
            autocomplete="nickname"
            [required]="true"
            [(value)]="nome"
          />

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
            autocomplete="new-password"
            hint="Pelo menos 12 caracteres. Frases longas funcionam bem e são fáceis de lembrar."
            [minLength]="12"
            [required]="true"
            [(value)]="senha"
          />

          <app-button type="submit" [loading]="enviando()" [fullWidth]="true">
            Criar minha conta
          </app-button>
        </form>

        <p class="apoio">Já tem conta? <a routerLink="/entrar">Entrar</a></p>
      }
    </app-card>
  `,
  styleUrl: './auth.scss',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);

  protected readonly nome = signal('');
  protected readonly email = signal('');
  protected readonly senha = signal('');
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly enviado = signal<string | null>(null);

  protected async cadastrar(event: Event): Promise<void> {
    event.preventDefault();
    this.enviando.set(true);
    this.erro.set(null);

    try {
      const resposta = await this.auth.register(this.email(), this.nome(), this.senha());

      // A mensagem é a mesma exista ou não a conta: a interface não pode revelar o
      // que a API esconde de propósito.
      this.enviado.set(resposta.message);
    } catch (falha) {
      this.erro.set((falha as ApiFailure).message ?? 'Não foi possível criar a conta.');
    } finally {
      this.enviando.set(false);
    }
  }
}
