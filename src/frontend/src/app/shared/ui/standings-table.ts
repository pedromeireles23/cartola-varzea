import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, TemplateRef, input } from '@angular/core';

import { Badge } from './badge';

/** Uma linha de classificação: a mesma forma no ranking geral e no da liga. */
export interface StandingRow {
  readonly position: number;
  readonly tied: boolean;
  readonly displayName: string;
  readonly totalPoints: number;
  readonly netWorth: number;
  readonly lastRoundPoints: number | null;
  readonly isViewer: boolean;
  /** Só na liga: quem a criou. */
  readonly isOwner?: boolean;
}

function numero(valor: number): string {
  return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Classificação em tabela (02 §8): colocação, participante, o que fez na última rodada,
 * patrimônio e pontos. Tabela de verdade, com cabeçalho de coluna e de linha, porque é
 * dado para comparar em coluna. No celular, última rodada e patrimônio descem para uma
 * linha de apoio embaixo do nome.
 *
 * A linha de quem lê é o que faz a pessoa voltar: ela se destaca com fundo, barra e a
 * etiqueta "Você", nunca só pela cor. `acao` acrescenta uma coluna por linha — é o
 * "Remover" do dono da liga, que só aparece no modo de gestão.
 */
@Component({
  selector: 'app-standings-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Badge, NgTemplateOutlet],
  template: `
    <table class="classificacao">
      <caption class="sr-only">
        {{
          legenda()
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col" class="col-posicao">
            <span aria-hidden="true">#</span><span class="sr-only">Colocação</span>
          </th>
          <th scope="col" class="col-nome">Participante</th>
          <th scope="col" class="col-numero col-extra">Última rodada</th>
          <th scope="col" class="col-numero col-extra">Patrimônio</th>
          <th scope="col" class="col-numero">Pontos</th>
          @if (acao()) {
            <th scope="col" class="col-acao"><span class="sr-only">Ações</span></th>
          }
        </tr>
      </thead>
      <tbody>
        @for (linha of linhas(); track linha.displayName + '-' + linha.position) {
          <tr class="linha" [class.linha--voce]="linha.isViewer" [style.--indice]="$index">
            <td class="linha__posicao num">{{ linha.position }}º</td>
            <th scope="row" class="linha__nome">
              {{ linha.displayName }}
              @if (linha.isOwner) {
                <app-badge>Dono</app-badge>
              }
              @if (linha.isViewer) {
                <app-badge tone="brand">Você</app-badge>
              }
              @if (linha.tied) {
                <span class="linha__empate">empatado</span>
              }
              <span class="linha__detalhe">
                {{ patrimonio(linha.netWorth) }}
                @if (rodadas() > 0) {
                  @if (linha.lastRoundPoints !== null) {
                    · {{ pontos(linha.lastRoundPoints) }} na última
                  } @else {
                    · não jogou a última
                  }
                }
              </span>
            </th>
            <td class="col-numero col-extra num">
              @if (rodadas() === 0) {
                <span aria-hidden="true">–</span><span class="sr-only">sem rodada apurada</span>
              } @else if (linha.lastRoundPoints === null) {
                <span aria-hidden="true">–</span><span class="sr-only">não jogou</span>
              } @else {
                {{ decimal(linha.lastRoundPoints) }}
              }
            </td>
            <td class="col-numero col-extra num">{{ patrimonio(linha.netWorth) }}</td>
            <td class="col-numero linha__pontos num">{{ pontos(linha.totalPoints) }}</td>
            @if (acao(); as modelo) {
              <td class="col-acao">
                <ng-container *ngTemplateOutlet="modelo; context: { $implicit: linha }" />
              </td>
            }
          </tr>
        }
      </tbody>
    </table>
  `,
  styleUrl: './standings-table.scss',
})
export class StandingsTable {
  readonly linhas = input.required<readonly StandingRow[]>();
  /** Rodadas apuradas; sem nenhuma, "última rodada" não tem o que mostrar. */
  readonly rodadas = input.required<number>();
  /** Legenda da tabela para o leitor de tela, como "Classificação de Copa da Vila". */
  readonly legenda = input.required<string>();
  readonly acao = input<TemplateRef<{ $implicit: StandingRow }> | null>(null);

  protected pontos(valor: number): string {
    return `${numero(valor)} pts`;
  }

  protected decimal(valor: number): string {
    return numero(valor);
  }

  protected patrimonio(valor: number): string {
    return `C$ ${numero(valor)}`;
  }
}
