import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PanelVariant = 'default' | 'elevated' | 'highlight' | 'operational' | 'compact';

const VARIANT_CLASS: Readonly<Record<PanelVariant, string>> = {
  default: '',
  elevated: 'painel--elevado',
  highlight: 'painel--destaque',
  operational: 'painel--operacional',
  compact: 'painel--compacto',
};

/**
 * Superfície funcional compartilhada. A variante escolhe hierarquia visual; o tema
 * escolhe as cores. O título sempre nomeia a section para navegação assistiva.
 */
@Component({
  selector: 'app-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section [class]="classes()" [attr.aria-labelledby]="heading() ? headingId() : null">
      @if (heading()) {
        <header class="painel__topo">
          <div>
            @if (supportingText()) {
              <p class="painel__apoio">{{ supportingText() }}</p>
            }
            @if (headingLevel() === 2) {
              <h2 class="painel__titulo" [id]="headingId()">{{ heading() }}</h2>
            } @else {
              <h3 class="painel__titulo" [id]="headingId()">{{ heading() }}</h3>
            }
          </div>
          <ng-content select="[panel-actions]" />
        </header>
      }
      <div class="painel__corpo"><ng-content /></div>
      <footer class="painel__rodape"><ng-content select="[panel-footer]" /></footer>
    </section>
  `,
  styleUrl: './panel.scss',
})
export class Panel {
  private static nextId = 0;

  readonly heading = input<string>();
  readonly supportingText = input<string>();
  readonly headingLevel = input<2 | 3>(2);
  readonly variant = input<PanelVariant>('default');

  private readonly instanceId = Panel.nextId++;
  protected readonly headingId = computed(() => `panel-heading-${this.instanceId}`);
  protected readonly classes = computed(() =>
    ['painel', VARIANT_CLASS[this.variant()]].filter(Boolean).join(' '),
  );
}
