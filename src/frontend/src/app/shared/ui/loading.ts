import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** O formato do que está carregando: texto corrido, cartões ou linhas de tabela. */
export type LoadingSkeleton = 'lines' | 'cards' | 'table';

/**
 * Indicador de carregamento (02 §8).
 *
 * A mensagem vai para leitor de tela via aria-live, para que a espera seja
 * percebida por quem nao ve a animacao.
 *
 * Com `skeleton` (V7), a espera desenha a forma do que vem — linhas, cartões ou tabela —
 * em vez do ponto pulsando, e a tela não pula quando o conteúdo chega. O rótulo continua
 * lá, só para o leitor de tela, e o brilho para com `prefers-reduced-motion`.
 */
@Component({
  selector: 'app-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (skeleton(); as forma) {
      <div class="esqueleto" [class]="'esqueleto--' + forma" role="status" aria-live="polite">
        <span class="sr-only">{{ label() }}</span>
        @switch (forma) {
          @case ('cards') {
            @for (item of [1, 2, 3]; track item) {
              <span class="esqueleto__cartao" aria-hidden="true"></span>
            }
          }
          @case ('table') {
            @for (item of [1, 2, 3, 4, 5]; track item) {
              <span class="esqueleto__fileira" aria-hidden="true"></span>
            }
          }
          @default {
            @for (largura of [100, 86, 64]; track $index) {
              <span class="esqueleto__linha" aria-hidden="true" [style.width.%]="largura"></span>
            }
          }
        }
      </div>
    } @else {
      <div class="loading" role="status" aria-live="polite">
        <span class="loading__dot" aria-hidden="true"></span>
        <span class="loading__label">{{ label() }}</span>
      </div>
    }
  `,
  styleUrl: './loading.scss',
})
export class Loading {
  readonly label = input('Carregando…');
  readonly skeleton = input<LoadingSkeleton | null>(null);
}
