import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

/**
 * Layout das areas sem login (02 §9.1). Navegacao enxuta, focada em descobrir
 * campeonatos.
 */
@Component({
  selector: 'app-public-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterOutlet],
  template: `
    <a class="skip" href="#conteudo">Pular para o conteúdo</a>

    <header class="topo">
      <div class="topo__interno">
        <a class="topo__marca" routerLink="/">Cartola Várzea</a>
        <nav class="topo__nav" aria-label="Navegação principal">
          <a routerLink="/sistema">Sistema</a>
        </nav>
      </div>
    </header>

    <main id="conteudo" class="conteudo">
      <router-outlet />
    </main>

    <footer class="rodape">
      <p>Demonstração de portfólio, com dados fictícios. Sem pagamentos, apostas ou premiações.</p>
    </footer>
  `,
  styleUrl: './public-layout.scss',
})
export class PublicLayout {}
