import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { DemoBanner } from '../demo-banner/demo-banner';
import { NotificationBell } from '../notification-bell/notification-bell';

/**
 * Layout das areas sem login (02 §9.1). Navegacao enxuta, focada em descobrir
 * campeonatos. Quando ha sessao, o acesso ao perfil aparece no lugar de entrar.
 *
 * _(2026-09-22, Fase 8.)_ "Estado do sistema" desceu para o rodape: e diagnostico de
 * portfolio, nao caminho de quem chega para entender o produto.
 */
@Component({
  selector: 'app-public-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DemoBanner, NotificationBell, RouterLink, RouterOutlet],
  template: `
    <a class="skip" href="#conteudo">Pular para o conteúdo</a>

    <header class="topo">
      <div class="topo__interno">
        <a class="topo__marca" routerLink="/">Cartola Várzea</a>
        <nav class="topo__nav" aria-label="Navegação principal">
          <a routerLink="/campeonatos">Campeonatos</a>
          @if (conta(); as dados) {
            <app-notification-bell />
            <a routerLink="/perfil">{{ dados.displayName }}</a>
          } @else {
            <a routerLink="/entrar">Entrar</a>
          }
        </nav>
      </div>
    </header>

    <app-demo-banner />

    <main id="conteudo" class="conteudo">
      <router-outlet />
    </main>

    <footer class="rodape">
      <p>Demonstração de portfólio, com dados fictícios. Sem pagamentos, apostas ou premiações.</p>
      <p><a routerLink="/sistema">Estado do sistema</a></p>
    </footer>
  `,
  styleUrl: './public-layout.scss',
})
export class PublicLayout {
  protected readonly conta = inject(AuthService).current;
}
