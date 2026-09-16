import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';

/**
 * Campo de formulario do design system (02 §8).
 *
 * O rotulo e sempre persistente e o placeholder e apenas exemplo. O erro fica
 * associado ao input por aria-describedby, nao apenas ao lado dele.
 */
@Component({
  selector: 'app-form-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="field">
      <label class="field__label" [for]="fieldId()">
        {{ label() }}
        @if (required()) {
          <span class="field__required">(obrigatório)</span>
        }
      </label>

      @if (multiline()) {
        <textarea
          class="field__input field__input--multiline"
          [id]="fieldId()"
          [name]="fieldId()"
          [rows]="rows()"
          [placeholder]="placeholder()"
          [required]="required()"
          [attr.minlength]="minLength()"
          [attr.maxlength]="maxLength()"
          [attr.aria-invalid]="error() ? 'true' : null"
          [attr.aria-describedby]="describedBy()"
          [value]="value()"
          (input)="onInput($event)"
        ></textarea>
      } @else {
        <input
          class="field__input"
          [id]="fieldId()"
          [name]="fieldId()"
          [type]="type()"
          [placeholder]="placeholder()"
          [required]="required()"
          [attr.autocomplete]="autocomplete()"
          [attr.minlength]="minLength()"
          [attr.maxlength]="maxLength()"
          [attr.aria-invalid]="error() ? 'true' : null"
          [attr.aria-describedby]="describedBy()"
          [value]="value()"
          (input)="onInput($event)"
        />
      }

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
export class FormField {
  private static nextId = 0;

  readonly label = input.required<string>();
  readonly type = input<'text' | 'email' | 'password' | 'number'>('text');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly error = input<string>();
  readonly hint = input<string>();
  readonly minLength = input<number>();
  readonly maxLength = input<number>();
  /** Texto livre em várias linhas, como motivos e observações. */
  readonly multiline = input(false);
  readonly rows = input(4);
  readonly value = model('');

  /**
   * Dica de preenchimento para o navegador e para gerenciadores de senha. Sem ela,
   * o autofill erra o campo e a pessoa acaba digitando tudo à mão.
   */
  readonly autocomplete = input<string>();

  private readonly instanceId = FormField.nextId++;

  protected readonly fieldId = computed(() => `field-${this.instanceId}`);
  protected readonly errorId = computed(() => `field-${this.instanceId}-error`);
  protected readonly hintId = computed(() => `field-${this.instanceId}-hint`);

  protected readonly describedBy = computed(() => {
    if (this.error()) {
      return this.errorId();
    }

    return this.hint() ? this.hintId() : null;
  });

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement | HTMLTextAreaElement).value);
  }
}
