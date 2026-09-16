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
        <h2 class="card__heading" [id]="headingId()">{{ heading() }}</h2>
      }
      <ng-content />
    </section>
  `,
  styleUrl: './card.scss',
})
export class Card {
  private static nextId = 0;

  readonly heading = input<string>();

  private readonly instanceId = Card.nextId++;

  protected readonly headingId = computed(() => `card-heading-${this.instanceId}`);
}
