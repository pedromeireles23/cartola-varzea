import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { PageMetaService } from '../../core/seo/page-meta';
import { Alert, BackLink, Button, Loading, PageHeader, StandingsTable } from '../../shared/ui';
import { StandingsTabs } from '../fantasy/standings-tabs';
import { Ranking, PublicCompetitionService } from './public-competition.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly ranking: Ranking }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

function numero(valor: number): string {
  return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Classificação geral acumulada do campeonato (01 §9, `/c/:campeonato/ranking`).
 *
 * É pública como o resto da página do campeonato: o visitante sem conta vê a tabela com
 * o nome de exibição de quem joga. Quem está logado se reconhece na tabela e vê, antes
 * dela, a própria posição e a distância para o líder — é a pergunta com que a pessoa
 * chega. As ligas privadas ficam na aba ao lado. Enquanto a última rodada está
 * provisória, o aviso fica junto dos números, como na pontuação da rodada (02 §8).
 */
@Component({
  selector: 'app-competition-ranking',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Alert,
    BackLink,
    Button,
    Loading,
    PageHeader,
    RouterLink,
    StandingsTable,
    StandingsTabs,
  ],
  template: `
    <app-back-link [link]="['/c', campeonato()]" label="Página do campeonato" />
    <app-page-header heading="Classificação" [subtitle]="ranking()?.competitionName" />
    <app-standings-tabs [campeonato]="campeonato()" atual="geral" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <section class="painel"><app-loading label="Montando a classificação…" /></section>
      }
      @case ('erro') {
        <section class="painel">
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </section>
      }
      @case ('pronto') {
        @let tabela = ranking()!;
        @if (tabela.rounds === 0) {
          <app-alert tone="info">
            Nenhuma rodada foi apurada ainda. A classificação começa a se mexer quando o primeiro
            resultado sair.
          </app-alert>
        } @else if (tabela.provisional) {
          <app-alert tone="warning">
            {{ tabela.lastRoundName }} ainda é provisória: a classificação pode mudar se a liga
            corrigir a súmula.
          </app-alert>
        } @else {
          <p class="apoio">
            {{ tabela.rounds }}
            {{ tabela.rounds === 1 ? 'rodada apurada' : 'rodadas apuradas' }}, até
            {{ tabela.lastRoundName }}.
          </p>
        }

        @if (minhaLinha(); as eu) {
          @if (tabela.rounds > 0) {
            <section class="resumo" aria-label="Sua posição">
              <div class="resumo__item">
                <p class="resumo__rotulo">Sua posição</p>
                <p class="resumo__valor num">
                  {{ eu.position }}º
                  <span class="resumo__apoio">de {{ tabela.entries.length }}</span>
                </p>
              </div>
              <div class="resumo__item">
                <p class="resumo__rotulo">Seus pontos</p>
                <p class="resumo__valor num">{{ pontos(eu.totalPoints) }}</p>
              </div>
              <div class="resumo__item">
                <p class="resumo__rotulo">Para o líder</p>
                <p class="resumo__valor num">{{ distancia() }}</p>
              </div>
            </section>
          }
        }

        @if (tabela.entries.length === 0) {
          <section class="painel">
            <p class="apoio">
              Ninguém entrou neste campeonato ainda. Quem escalar primeiro abre a classificação.
            </p>
            <a class="acao" [routerLink]="['/c', campeonato(), 'jogar']">Jogar neste campeonato</a>
          </section>
        } @else {
          <app-standings-table
            [linhas]="tabela.entries"
            [rodadas]="tabela.rounds"
            [legenda]="'Classificação geral de ' + tabela.competitionName"
          />
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './competition-ranking.scss'],
})
export class CompetitionRankingPage implements OnInit {
  private readonly service = inject(PublicCompetitionService);
  private readonly meta = inject(PageMetaService);

  readonly campeonato = input.required<string>();
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly ranking = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.ranking : null;
  });

  protected readonly minhaLinha = computed(
    () => this.ranking()?.entries.find((linha) => linha.isViewer) ?? null,
  );

  /** "Você lidera" em vez de "0,00 pts": estar na frente não é uma distância. */
  protected readonly distancia = computed(() => {
    const lider = this.ranking()?.entries[0];
    const eu = this.minhaLinha();
    if (!lider || !eu) {
      return '';
    }
    const diferenca = lider.totalPoints - eu.totalPoints;
    return eu.position === 1 || diferenca <= 0 ? 'Você lidera' : `${numero(diferenca)} pts`;
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
        this.meta.set({
          title: `Ranking de ${ranking.competitionName}`,
          description: `Classificação geral acumulada de ${ranking.competitionName}, com pontos, patrimônio e o que cada pessoa fez na última rodada apurada.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected pontos(valor: number): string {
    return `${numero(valor)} pts`;
  }
}
