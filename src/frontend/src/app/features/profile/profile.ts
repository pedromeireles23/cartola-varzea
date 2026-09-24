import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { Alert, Badge, Button, PageHeader } from '../../shared/ui';
import { OrganizerArea } from '../organizer/organizer-area';

/**
 * Conta (06 §4.1 e Parte 4, `/perfil`).
 *
 * A navegação chama esta tela de "Conta", e o título acompanha. Ela deixou de ser o
 * destino depois da entrada — quem entra vai para o início — e ficou com o que é da
 * conta: os dados, a sessão e as portas para as outras áreas, cada uma só para quem
 * pode usá-la.
 */
@Component({
  selector: 'app-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, PageHeader, RouterLink],
  template: `
    <app-page-header heading="Conta" />

    @if (conta(); as dados) {
      <section class="painel" aria-labelledby="dados-titulo">
        <header class="painel__topo">
          <h2 id="dados-titulo" class="painel__titulo">Seus dados</h2>
        </header>
        <div class="painel__corpo">
          <div class="identidade">
            <span class="avatar" aria-hidden="true">{{ iniciais() }}</span>
            <p class="identidade__nome">{{ dados.displayName }}</p>
          </div>
          <dl class="dados">
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
        </div>
        <footer class="painel__rodape">
          <p class="apoio">A sessão vale só neste navegador.</p>
          <app-button variant="secondary" [loading]="saindo()" (pressed)="sair()">
            Encerrar sessão
          </app-button>
        </footer>
      </section>
    } @else {
      <app-alert tone="info">Sessão não encontrada.</app-alert>
    }

    <section class="painel" aria-labelledby="organizar-titulo">
      <header class="painel__topo">
        <h2 id="organizar-titulo" class="painel__titulo">Organizar campeonatos</h2>
      </header>
      <div class="painel__corpo">
        @if (organiza()) {
          <p class="apoio">
            Times, atletas, rodadas e súmulas ficam na área de organização, separada do jogo.
          </p>
          <div class="acoes">
            <a class="acao" routerLink="/organizar">Minhas organizações</a>
            <a class="acao acao--discreta" routerLink="/organizar/solicitar">
              Pedir acesso para outra organização
            </a>
          </div>
        } @else {
          <p class="apoio">
            Organiza uma liga ou campeonato amador? Peça acesso para cadastrar times, atletas e
            súmulas.
          </p>
          <a class="acao" routerLink="/organizar/solicitar">Quero organizar</a>
        }
      </div>
    </section>

    @if (administra()) {
      <section class="painel" aria-labelledby="admin-titulo">
        <header class="painel__topo">
          <h2 id="admin-titulo" class="painel__titulo">Administração da plataforma</h2>
        </header>
        <div class="painel__corpo">
          <p class="apoio">Analise os pedidos de quem quer organizar campeonatos.</p>
          <a class="acao" routerLink="/admin/solicitacoes">Ver solicitações</a>
        </div>
      </section>
    }
  `,
  styleUrls: ['../../shared/ui/panel.scss', './profile.scss'],
})
export class ProfilePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly conta = this.auth.current;
  protected readonly administra = this.auth.isPlatformAdmin;
  protected readonly organiza = inject(OrganizerArea).canOrganize;
  protected readonly saindo = signal(false);

  protected readonly iniciais = computed(() =>
    (this.conta()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((parte) => parte[0])
      .join('')
      .toLocaleUpperCase('pt-BR'),
  );

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
