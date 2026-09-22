import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../shared/ui';
import { Ranking, PublicCompetitionService } from './public-competition.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly ranking: Ranking }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Ranking geral acumulado do campeonato (01 §9, `/c/:campeonato/ranking`).
 *
 * É público como o resto da página do campeonato: o visitante sem conta vê a
 * classificação com o nome de exibição de quem joga. Quem está logado se reconhece na
 * lista, que é o que faz a pessoa voltar. Enquanto a última rodada está provisória, o
 * aviso fica junto dos números, como na pontuação da rodada (02 §8).
 */
@Component({
  selector: 'app-competition-ranking',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato()]">← Voltar ao campeonato</a></p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Ranking</h1>
        <app-card><app-loading label="Montando a classificação…" /></app-card>
      }
      @case ('erro') {
        <h1>Ranking</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>Ranking de {{ ranking()!.competitionName }}</h1>

        @if (ranking()!.rounds === 0) {
          <app-alert tone="info">
            Nenhuma rodada foi apurada ainda. A classificação começa a se mexer quando o primeiro
            resultado sair.
          </app-alert>
        } @else if (ranking()!.provisional) {
          <app-alert tone="warning">
            {{ ranking()!.lastRoundName }} ainda é provisória: a classificação pode mudar se a liga
            corrigir a súmula.
          </app-alert>
        } @else {
          <p class="apoio">
            {{ ranking()!.rounds }}
            {{ ranking()!.rounds === 1 ? 'rodada apurada' : 'rodadas apuradas' }}, até
            {{ ranking()!.lastRoundName }}.
          </p>
        }

        @if (ranking()!.entries.length === 0) {
          <app-card>
            <p class="apoio">
              Ninguém entrou neste campeonato ainda. Quem escalar primeiro abre a classificação.
            </p>
            <a class="acao" [routerLink]="['/c', campeonato(), 'jogar']">Jogar neste campeonato</a>
          </app-card>
        } @else {
          <app-card>
            <ol class="classificacao">
              @for (linha of ranking()!.entries; track linha.displayName + '-' + linha.position) {
                <li class="linha" [class.linha--voce]="linha.isViewer">
                  <span class="linha__posicao" [attr.aria-label]="colocacao(linha.position)">
                    {{ linha.position }}º
                  </span>
                  <span class="linha__nome">
                    {{ linha.displayName }}
                    @if (linha.isViewer) {
                      <app-badge tone="brand">Você</app-badge>
                    }
                    <span class="linha__detalhe">
                      {{ patrimonio(linha.netWorth) }}
                      @if (ranking()!.rounds > 0) {
                        @if (linha.lastRoundPoints !== null) {
                          · {{ pontos(linha.lastRoundPoints) }} na última
                        } @else {
                          · não jogou a última
                        }
                      }
                      @if (linha.tied) {
                        · empatado
                      }
                    </span>
                  </span>
                  <strong class="linha__pontos">{{ pontos(linha.totalPoints) }}</strong>
                </li>
              }
            </ol>
          </app-card>
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './competition-ranking.scss'],
})
export class CompetitionRankingPage implements OnInit {
  private readonly service = inject(PublicCompetitionService);
  private readonly title = inject(Title);

  readonly campeonato = input.required<string>();
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly ranking = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.ranking : null;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.ranking(this.campeonato()).subscribe({
      next: (ranking) => {
        this.estado.set({ tipo: 'pronto', ranking });
        this.title.setTitle(`Ranking de ${ranking.competitionName} · Cartola Várzea`);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  /** "3º" sai como "terceiro lugar" no leitor de tela, que não lê o º sozinho. */
  protected colocacao(posicao: number): string {
    return `${posicao}º lugar`;
  }

  protected pontos(valor: number): string {
    return `${this.numero(valor)} pts`;
  }

  protected patrimonio(valor: number): string {
    return `C$ ${this.numero(valor)}`;
  }

  private numero(valor: number): string {
    return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
}
