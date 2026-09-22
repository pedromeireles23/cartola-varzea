import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/**
 * Botao do design system (02 §8).
 *
 * Em carregamento o rotulo permanece visivel e o clique fica bloqueado, para que
 * o usuario nao perca o contexto da acao nem envie duas vezes.
 */
@Component({
  selector: 'app-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [type]="type()"
      [class]="classes()"
      [disabled]="disabled() || loading()"
      [attr.aria-busy]="loading() ? 'true' : null"
      [attr.aria-expanded]="expanded() === undefined ? null : expanded()"
      [attr.aria-controls]="controls() ?? null"
      (click)="pressed.emit()"
    >
      @if (loading()) {
        <span class="spinner" aria-hidden="true"></span>
      }
      <ng-content />
    </button>
  `,
  styleUrl: './button.scss',
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly type = input<'button' | 'submit'>('button');
  readonly disabled = input(false);
  readonly loading = input(false);
  readonly fullWidth = input(false);

  /**
   * Quando o botao abre um painel na propria tela, `expanded` e `controls` ligam os
   * dois: sem isso o leitor de tela anuncia so "botao", sem dizer se ja esta aberto.
   */
  readonly expanded = input<boolean | undefined>(undefined);
  readonly controls = input<string>();

  readonly pressed = output<void>();

  protected readonly classes = computed(() =>
    ['btn', `btn--${this.variant()}`, this.fullWidth() ? 'btn--block' : '']
      .filter(Boolean)
      .join(' '),
  );
}
