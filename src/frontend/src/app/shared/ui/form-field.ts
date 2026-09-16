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

      <input
        class="field__input"
        [id]="fieldId()"
        [type]="type()"
        [placeholder]="placeholder()"
        [required]="required()"
        [attr.aria-invalid]="error() ? 'true' : null"
        [attr.aria-describedby]="error() ? errorId() : null"
        [value]="value()"
        (input)="onInput($event)"
      />

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
  readonly value = model('');

  private readonly instanceId = FormField.nextId++;

  protected readonly fieldId = computed(() => `field-${this.instanceId}`);
  protected readonly errorId = computed(() => `field-${this.instanceId}-error`);

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }
}
