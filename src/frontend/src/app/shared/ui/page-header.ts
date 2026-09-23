import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Cabeçalho de página da área autenticada (06 §3.1 e §7): título contido, uma linha de
 * contexto e, à direita, as ações locais. Sem hero nem slogan: a primeira dobra é da
 * tarefa, não da apresentação.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cabecalho__texto">
      @if (kicker()) {
        <p class="cabecalho__apoio">{{ kicker() }}</p>
      }
      <h1 class="cabecalho__titulo">{{ heading() }}</h1>
      @if (subtitle()) {
        <p class="cabecalho__subtitulo">{{ subtitle() }}</p>
      }
    </div>
    <div class="cabecalho__acoes"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3) var(--space-5);
      align-items: flex-end;
      justify-content: space-between;
    }

    .cabecalho__texto {
      display: grid;
      gap: var(--space-1);
      min-width: 0;
    }

    .cabecalho__apoio {
      color: var(--color-ink-500);
      font-size: var(--font-caption);
      font-weight: 500;
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }

    .cabecalho__subtitulo {
      color: var(--color-ink-500);
    }

    .cabecalho__acoes {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .cabecalho__acoes:empty {
      display: none;
    }
  `,
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly subtitle = input<string | null>();
  /** Linha curta acima do título, como "Rodada 4". */
  readonly kicker = input<string | null>();
}
