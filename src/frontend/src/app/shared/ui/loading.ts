import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Indicador de carregamento (02 §8).
 *
 * A mensagem vai para leitor de tela via aria-live, para que a espera seja
 * percebida por quem nao ve a animacao.
 */
@Component({
  selector: 'app-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading" role="status" aria-live="polite">
      <span class="loading__dot" aria-hidden="true"></span>
      <span class="loading__label">{{ label() }}</span>
    </div>
  `,
  styleUrl: './loading.scss',
})
export class Loading {
  readonly label = input('Carregando…');
}
