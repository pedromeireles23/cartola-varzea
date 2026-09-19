import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * Navegação entre as telas do jogo num campeonato (02 §8 e §9.1): Início, Mercado e
 * Escalação, nessa ordem. Ligas entram com a Fase 11. A tela atual é marcada com
 * `aria-current`, não só com cor.
 */
@Component({
  selector: 'app-fantasy-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="jogo-nav" aria-label="Navegação do jogo">
      @for (item of itens; track item.caminho) {
        <a
          class="jogo-nav__link"
          [routerLink]="['/c', campeonato(), item.caminho]"
          routerLinkActive="jogo-nav__link--atual"
          ariaCurrentWhenActive="page"
          >{{ item.rotulo }}</a
        >
      }
    </nav>
  `,
  styleUrl: './fantasy-nav.scss',
})
export class FantasyNav {
  /** Slug do campeonato. */
  readonly campeonato = input.required<string>();

  protected readonly itens = [
    { caminho: 'jogar', rotulo: 'Início' },
    { caminho: 'mercado', rotulo: 'Mercado' },
    { caminho: 'escalacao', rotulo: 'Escalação' },
  ] as const;
}
