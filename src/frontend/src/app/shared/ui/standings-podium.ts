import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { Badge } from './badge';
import { StandingRow } from './standings-table';

function numero(valor: number): string {
  return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Resumo dos três primeiros antes da tabela completa. A ordem continua sendo uma lista
 * ordenada e posição, nome e pontos aparecem em texto: o destaque nunca depende só da cor.
 */
@Component({
  selector: 'app-standings-podium',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Badge],
  template: `
    @if (lideres().length > 0) {
      <section class="podio" [attr.aria-label]="legenda()">
        <ol class="podio__lista">
          @for (linha of lideres(); track linha.displayName + '-' + linha.position) {
            <li class="podio__lugar" [class.podio__lugar--lider]="linha.position === 1">
              <span class="podio__posicao num">{{ linha.position }}º</span>
              <span class="podio__nome">
                {{ linha.displayName }}
                @if (linha.isViewer) {
                  <app-badge tone="brand">Você</app-badge>
                }
              </span>
              <strong class="podio__pontos num">{{ pontos(linha.totalPoints) }}</strong>
              @if (linha.tied) {
                <span class="podio__empate">Posição empatada</span>
              }
            </li>
          }
        </ol>
      </section>
    }
  `,
  styleUrl: './standings-podium.scss',
})
export class StandingsPodium {
  readonly linhas = input.required<readonly StandingRow[]>();
  readonly legenda = input('Pódio da classificação');

  protected readonly lideres = computed(() => this.linhas().slice(0, 3));

  protected pontos(valor: number): string {
    return `${numero(valor)} pts`;
  }
}
