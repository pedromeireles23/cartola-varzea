import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';

import { READ_ONLY_MODE, READ_ONLY_NOTICE_ID } from '../../core/demo/read-only-mode';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/**
 * Botao do design system (02 §8).
 *
 * Em carregamento o rotulo permanece visivel e o clique fica bloqueado, para que
 * o usuario nao perca o contexto da acao nem envie duas vezes.
 *
 * Com `escrita`, o botão sabe que grava algo: na conta de demonstração ele fica
 * indisponível com `aria-disabled` — e não `disabled`, que o tiraria da ordem do teclado
 * e esconderia o motivo —, aponta para o aviso da faixa de demonstração e não dispara
 * nem submete o formulário (02 §9.1).
 */
@Component({
  selector: 'app-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [type]="type()"
      [class]="classes()"
      [disabled]="disabled() || loading()"
      [attr.aria-disabled]="blocked() ? 'true' : null"
      [attr.aria-describedby]="blocked() ? noticeId : null"
      [attr.title]="blocked() ? 'Indisponível no modo demonstração' : null"
      [attr.aria-busy]="loading() ? 'true' : null"
      [attr.aria-expanded]="expanded() === undefined ? null : expanded()"
      [attr.aria-controls]="controls() ?? null"
      (click)="press($event)"
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

  /** A ação grava algo; na conta de demonstração fica indisponível. */
  readonly escrita = input(false, { transform: booleanAttribute });

  readonly pressed = output<void>();

  private readonly readOnly = inject(READ_ONLY_MODE);

  protected readonly noticeId = READ_ONLY_NOTICE_ID;
  protected readonly blocked = computed(() => this.escrita() && this.readOnly());

  protected press(event: MouseEvent): void {
    if (this.blocked()) {
      // Também cancela o envio do formulário, inclusive o do Enter num campo.
      event.preventDefault();
      return;
    }
    this.pressed.emit();
  }

  protected readonly classes = computed(() =>
    ['btn', `btn--${this.variant()}`, this.fullWidth() ? 'btn--block' : '']
      .filter(Boolean)
      .join(' '),
  );
}
