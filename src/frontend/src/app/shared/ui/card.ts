import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Cartao do design system. Usa borda; sombra nao e obrigatoria (02 §6).
 *
 * Quando tem titulo, o <section> e associado a ele por aria-labelledby. Sem isso o
 * navegador nao expoe a secao como landmark e quem usa leitor de tela nao consegue
 * navegar entre os blocos da pagina.
 */
@Component({
  selector: 'app-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="card" [attr.aria-labelledby]="heading() ? headingId() : null">
      @if (heading()) {
        @if (headingLevel() === 1) {
          <h1 class="card__heading" [id]="headingId()">{{ heading() }}</h1>
        } @else {
          <h2 class="card__heading" [id]="headingId()">{{ heading() }}</h2>
        }
      }
      <ng-content />
    </section>
  `,
  styleUrl: './card.scss',
})
export class Card {
  private static nextId = 0;

  readonly heading = input<string>();

  /**
   * Nivel do titulo. Quando o cartao e o assunto da pagina inteira — as telas de conta,
   * por exemplo —, ele precisa ser o `h1`: uma pagina que comeca em `h2` quebra a ordem
   * de cabecalho e deixa quem usa leitor de tela sem o titulo principal (02 §12).
   */
  readonly headingLevel = input<1 | 2>(2);

  private readonly instanceId = Card.nextId++;

  protected readonly headingId = computed(() => `card-heading-${this.instanceId}`);
}
