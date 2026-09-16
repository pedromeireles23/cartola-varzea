import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/**
 * Layout das areas autenticadas do participante (02 §9.1).
 *
 * No mobile a navegacao fica numa barra inferior com Inicio, Mercado, Escalacao,
 * Ligas e Perfil. As rotas ainda nao existem: entram nas Fases 5 e 9. O layout
 * vem antes porque o corte vertical precisa provar as duas cascas.
 */
@Component({
  selector: 'app-app-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <a class="skip" href="#conteudo">Pular para o conteúdo</a>

    <main id="conteudo" class="conteudo">
      <router-outlet />
    </main>

    <nav class="barra" aria-label="Navegação do participante">
      @for (item of items; track item.rota) {
        <a class="barra__item" [routerLink]="item.rota" routerLinkActive="barra__item--ativo">
          {{ item.rotulo }}
        </a>
      }
    </nav>
  `,
  styleUrl: './app-layout.scss',
})
export class AppLayout {
  protected readonly items = [{ rotulo: 'Sistema', rota: '/sistema' }] as const;
}
