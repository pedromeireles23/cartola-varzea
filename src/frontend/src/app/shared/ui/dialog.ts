import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';

/**
 * Dialogo de confirmacao (02 §8).
 *
 * Usa o <dialog> nativo com showModal(): o navegador ja prende o foco, devolve ao
 * elemento anterior e trata Esc, que sao requisitos do 02 §12.
 */
@Component({
  selector: 'app-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog #dialog class="dialog" [attr.aria-labelledby]="headingId" (close)="dismissed.emit()">
      <h2 class="dialog__heading" [id]="headingId">{{ heading() }}</h2>
      <div class="dialog__body"><ng-content /></div>
      <div class="dialog__actions"><ng-content select="[dialogActions]" /></div>
    </dialog>
  `,
  styleUrl: './dialog.scss',
})
export class Dialog {
  private static nextId = 0;

  /** Liga o título ao diálogo para que o leitor de tela anuncie o que está sendo decidido. */
  protected readonly headingId = `dialog-heading-${Dialog.nextId++}`;

  readonly heading = input.required<string>();
  readonly open = input(false);

  readonly dismissed = output<void>();

  private readonly dialogRef = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const element = this.dialogRef().nativeElement;

      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }
}
