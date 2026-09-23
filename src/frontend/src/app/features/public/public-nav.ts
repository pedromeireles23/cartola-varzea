import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** As seções públicas de um campeonato. */
type Secao = 'visao' | 'tabela' | 'partidas' | 'ranking';

/**
 * Navegação entre as seções públicas de um campeonato (02 §9.1).
 *
 * Não usa `routerLinkActive`: a seção atual é dita pela página, porque a súmula de uma
 * partida também pertence a "Partidas" e o casamento por prefixo não alcança isso
 * sozinho de forma confiável.
 */
@Component({
  selector: 'app-public-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <nav class="secoes" aria-label="Seções do campeonato">
      @for (item of itens(); track item.secao) {
        <a
          class="secoes__link"
          [class.secoes__link--atual]="item.secao === atual()"
          [attr.aria-current]="item.secao === atual() ? 'page' : null"
          [routerLink]="item.caminho"
          >{{ item.rotulo }}</a
        >
      }
    </nav>
  `,
  styleUrl: './public-nav.scss',
})
export class PublicNav {
  /** Slug do campeonato. */
  readonly campeonato = input.required<string>();
  readonly atual = input.required<Secao>();

  protected readonly itens = computed(() => {
    const slug = this.campeonato();
    return [
      { secao: 'visao' as const, rotulo: 'Visão geral', caminho: ['/c', slug] },
      { secao: 'tabela' as const, rotulo: 'Tabela', caminho: ['/c', slug, 'tabela'] },
      { secao: 'partidas' as const, rotulo: 'Partidas', caminho: ['/c', slug, 'partidas'] },
      { secao: 'ranking' as const, rotulo: 'Ranking', caminho: ['/c', slug, 'ranking'] },
    ];
  });
}
