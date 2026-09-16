import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';

export interface SelectOption {
  readonly value: string;
  readonly label: string;
}

/**
 * Lista de opções do design system (02 §8), sobre o `<select>` nativo.
 *
 * O controle nativo basta enquanto as listas forem curtas e sem busca: já funciona
 * com teclado, leitor de tela e o seletor próprio do celular. Rótulo, dica e erro
 * seguem o mesmo contrato do campo de texto.
 */
@Component({
  selector: 'app-select-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="field">
      <label class="field__label" [for]="fieldId()">{{ label() }}</label>

      <select
        class="field__input"
        [id]="fieldId()"
        [name]="fieldId()"
        [disabled]="disabled()"
        [attr.aria-invalid]="error() ? 'true' : null"
        [attr.aria-describedby]="describedBy()"
        (change)="onChange($event)"
      >
        @for (option of options(); track option.value) {
          <option [value]="option.value" [selected]="option.value === value()">
            {{ option.label }}
          </option>
        }
      </select>

      @if (hint() && !error()) {
        <p class="field__hint" [id]="hintId()">{{ hint() }}</p>
      }

      @if (error()) {
        <p class="field__error" [id]="errorId()">{{ error() }}</p>
      }
    </div>
  `,
  styleUrl: './form-field.scss',
})
export class SelectField {
  private static nextId = 0;

  readonly label = input.required<string>();
  readonly options = input.required<readonly SelectOption[]>();
  readonly hint = input<string>();
  readonly error = input<string>();
  readonly disabled = input(false);
  readonly value = model('');

  private readonly instanceId = SelectField.nextId++;

  protected readonly fieldId = computed(() => `select-${this.instanceId}`);
  protected readonly errorId = computed(() => `select-${this.instanceId}-error`);
  protected readonly hintId = computed(() => `select-${this.instanceId}-hint`);

  protected readonly describedBy = computed(() => {
    if (this.error()) {
      return this.errorId();
    }

    return this.hint() ? this.hintId() : null;
  });

  protected onChange(event: Event): void {
    this.value.set((event.target as HTMLSelectElement).value);
  }
}
