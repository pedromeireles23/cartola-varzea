import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PageHeaderVariant = 'default' | 'game' | 'operational';

/**
 * Cabeçalho de página com hierarquia escolhida explicitamente pelo contexto. A variante
 * visual nunca muda a semântica: por padrão continua sendo o h1 da página.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'classes()' },
  template: `
    <div class="cabecalho__texto">
      @if (kicker()) {
        <p class="cabecalho__apoio">{{ kicker() }}</p>
      }
      @if (headingLevel() === 1) {
        <h1 class="cabecalho__titulo">{{ heading() }}</h1>
      } @else {
        <h2 class="cabecalho__titulo">{{ heading() }}</h2>
      }
      @if (subtitle()) {
        <p class="cabecalho__subtitulo">{{ subtitle() }}</p>
      }
    </div>
    <div class="cabecalho__acoes"><ng-content /></div>
  `,
  styleUrl: './page-header.scss',
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly subtitle = input<string | null>();
  /** Linha curta acima do título, como "Rodada 4". */
  readonly kicker = input<string | null>();
  readonly variant = input<PageHeaderVariant>('default');
  readonly headingLevel = input<1 | 2>(1);

  protected readonly classes = computed(() => `page-header page-header--${this.variant()}`);
}
