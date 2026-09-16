import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { Alert, Badge, Button, Card } from '../../shared/ui';

@Component({
  selector: 'app-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, RouterLink],
  template: `
    <h1>Seu perfil</h1>

    <app-card heading="Conta">
      @if (conta(); as dados) {
        <dl class="dados">
          <div class="dados__item">
            <dt>Nome</dt>
            <dd>{{ dados.displayName }}</dd>
          </div>
          <div class="dados__item">
            <dt>E-mail</dt>
            <dd>{{ dados.email }}</dd>
          </div>
          <div class="dados__item">
            <dt>Verificação</dt>
            <dd>
              @if (dados.emailConfirmed) {
                <app-badge tone="success">E-mail confirmado</app-badge>
              } @else {
                <app-badge tone="warning">E-mail pendente</app-badge>
              }
            </dd>
          </div>
        </dl>
      } @else {
        <app-alert tone="info">Sessão não encontrada.</app-alert>
      }

      <app-button variant="secondary" [loading]="saindo()" (pressed)="sair()">
        Encerrar sessão
      </app-button>
    </app-card>

    <app-card heading="Organizar campeonatos">
      <p class="apoio">
        Organiza uma liga ou campeonato amador? Peça acesso para cadastrar times, atletas e súmulas.
      </p>
      <div class="acoes">
        <a class="acao" routerLink="/organizar">Minhas organizações</a>
        <a class="acao" routerLink="/organizar/solicitar">Quero organizar</a>
      </div>
    </app-card>

    @if (administra()) {
      <app-card heading="Administração da plataforma">
        <p class="apoio">Analise os pedidos de quem quer organizar campeonatos.</p>
        <a class="acao" routerLink="/admin/solicitacoes">Ver solicitações</a>
      </app-card>
    }
  `,
  styleUrl: './profile.scss',
})
export class ProfilePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly conta = this.auth.current;
  protected readonly administra = this.auth.isPlatformAdmin;
  protected readonly saindo = signal(false);

  protected async sair(): Promise<void> {
    this.saindo.set(true);

    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/entrar');
    } finally {
      this.saindo.set(false);
    }
  }
}
