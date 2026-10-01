import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Qual desenho acompanha o vazio. Cada um fala de uma situação do futebol de várzea:
 * - `campo`: nada aqui ainda — o campo está marcado, falta o jogo;
 * - `tatica`: a busca ou o filtro não achou ninguém — volta para a prancheta;
 * - `arquibancada`: ninguém entrou ainda — a torcida está vazia;
 * - `placar`: o resultado ainda não saiu ou não está disponível.
 */
export type EmptyIllustration = 'campo' | 'tatica' | 'arquibancada' | 'placar';

/**
 * Estado vazio do design system (V7): um desenho próprio, um título curto e o texto que
 * diz o que falta e o que fazer. O desenho é traço em SVG, sem fotografia nem ativo de
 * terceiros, e fica fora da árvore de acessibilidade — o texto já diz tudo.
 *
 * O título não é um heading: o vazio mora dentro de uma seção que já tem o seu, e um
 * heading a mais quebraria a hierarquia. Ações vão no slot `[acoes]`.
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="vazio" [class.vazio--compacto]="compacto()">
      @switch (ilustracao()) {
        @case ('tatica') {
          <svg class="vazio__desenho" viewBox="0 0 160 96" aria-hidden="true" focusable="false">
            <rect class="traco" x="6" y="6" width="148" height="84" rx="6" />
            <path class="traco traco--fino" d="M80 6V90" />
            <circle class="traco traco--fino" cx="80" cy="48" r="14" />
            <path class="marca" d="M34 30l10 10m0-10L34 40" />
            <path class="marca" d="M34 58l10 10m0-10L34 68" />
            <circle class="destaque" cx="118" cy="36" r="6" />
            <circle class="destaque" cx="118" cy="62" r="6" />
            <path class="seta" d="M48 48C66 26 94 24 108 34" />
            <path class="seta-ponta" d="M102 28l7 6-9 3" />
          </svg>
        }
        @case ('arquibancada') {
          <svg class="vazio__desenho" viewBox="0 0 160 96" aria-hidden="true" focusable="false">
            <path class="traco" d="M10 30H150M10 50H150M10 70H150" />
            <path class="traco traco--fino" d="M10 30V86M150 30V86" />
            <circle class="ponto" cx="30" cy="24" r="4" />
            <circle class="ponto" cx="62" cy="44" r="4" />
            <circle class="destaque" cx="96" cy="24" r="5" />
            <circle class="ponto" cx="122" cy="64" r="4" />
            <path class="traco traco--fino" d="M10 86H150" />
          </svg>
        }
        @case ('placar') {
          <svg class="vazio__desenho" viewBox="0 0 160 96" aria-hidden="true" focusable="false">
            <rect class="traco" x="14" y="16" width="132" height="62" rx="8" />
            <rect class="placa" x="30" y="30" width="36" height="34" rx="4" />
            <rect class="placa" x="94" y="30" width="36" height="34" rx="4" />
            <path class="marca" d="M40 47H56M104 47H120" />
            <path class="traco traco--fino" d="M76 44l8 8m0-8l-8 8" />
            <path class="traco" d="M64 78V88M96 78V88" />
          </svg>
        }
        @default {
          <svg class="vazio__desenho" viewBox="0 0 160 96" aria-hidden="true" focusable="false">
            <rect class="traco" x="6" y="6" width="148" height="84" rx="4" />
            <path class="traco traco--fino" d="M80 6V90" />
            <circle class="traco traco--fino" cx="80" cy="48" r="16" />
            <path class="traco traco--fino" d="M6 28H28V68H6M154 28H132V68H154" />
            <circle class="destaque" cx="80" cy="48" r="4" />
          </svg>
        }
      }
      <div class="vazio__texto">
        @if (titulo()) {
          <p class="vazio__titulo">{{ titulo() }}</p>
        }
        <div class="vazio__corpo"><ng-content /></div>
        <div class="vazio__acoes"><ng-content select="[acoes]" /></div>
      </div>
    </div>
  `,
  styleUrl: './empty-state.scss',
})
export class EmptyState {
  readonly titulo = input<string>();
  readonly ilustracao = input<EmptyIllustration>('campo');
  /** Vazio dentro de uma lista ou filtro: desenho menor, mesma mensagem. */
  readonly compacto = input(false);
}
