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

import { Loading } from '../../shared/ui';
import { kickoffText, points } from '../fantasy/fantasy-format';
import { MODALITY_LABELS } from '../organizer/competition.service';
import {
  PublicCompetitionService,
  PublicCompetitionSummary,
  Ranking,
} from './public-competition.service';
import { PublicFixtureService, PublicRound, ROUND_PHASE_LABELS } from './public-fixture.service';
import { lastPublishedRound, roundsInProgress, upcomingMatches } from './public-rounds';

/** Quantas partidas e quantos líderes cabem no cartaz sem virar a página do campeonato. */
const PARTIDAS = 4;
const LIDERES = 3;

type Carga<T> =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly dados: T }
  | { readonly tipo: 'erro' };

/**
 * O campeonato em destaque na landing (V5): o mais recente publicado, contado como um
 * cartaz de jogo — o que está em andamento, o último placar e quem lidera o fantasy.
 *
 * Tudo vem das rotas públicas, as mesmas da página do campeonato; nenhum número é
 * ilustrativo. Calendário e ranking são acessórios: se um falhar, o cartaz continua de
 * pé com o nome, os metadados e o caminho para o campeonato.
 */
@Component({
  selector: 'app-landing-featured',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Loading, RouterLink],
  template: `
    <article class="cartaz" aria-labelledby="cartaz-titulo">
      <header class="cartaz__topo">
        <p class="cartaz__eyebrow">Em destaque</p>
        <h3 id="cartaz-titulo" class="cartaz__titulo">
          <a [routerLink]="['/c', campeonato().slug]">{{ campeonato().name }}</a>
        </h3>
        <p class="cartaz__meta">
          {{ modalidade() }} · Temporada {{ campeonato().season }} ·
          {{ campeonato().organizationName }}
        </p>
        @if (rodadasAgora().length > 0) {
          <ul class="cartaz__agora" aria-label="Rodadas em andamento">
            @for (rodada of rodadasAgora(); track rodada.id) {
              <li
                class="fase"
                [class.fase--aberta]="rodada.phase === 'MarketOpen' && !rodada.underCorrection"
                [class.fase--correcao]="rodada.underCorrection"
              >
                <span class="fase__ponto" aria-hidden="true"></span>
                <span
                  ><strong>{{ rodada.name }}</strong> · {{ situacao(rodada) }}</span
                >
              </li>
            }
          </ul>
        }
      </header>

      <div class="cartaz__corpo">
        <section class="painel-jogos" aria-labelledby="cartaz-jogos">
          @switch (calendario().tipo) {
            @case ('carregando') {
              <h4 id="cartaz-jogos" class="cartaz__subtitulo">Partidas</h4>
              <app-loading label="Buscando as partidas…" skeleton="lines" />
            }
            @case ('erro') {
              <h4 id="cartaz-jogos" class="cartaz__subtitulo">Partidas</h4>
              <p class="cartaz__apoio">O calendário não carregou agora; ele está no campeonato.</p>
            }
            @case ('pronto') {
              @if (resultado(); as rodada) {
                <h4 id="cartaz-jogos" class="cartaz__subtitulo">
                  Último resultado · {{ rodada.name }}
                </h4>
                @if (rodada.provisional) {
                  <p class="cartaz__apoio">Provisório: pode mudar até a rodada consolidar.</p>
                }
                <ul class="placares">
                  @for (jogo of placares(); track jogo.id) {
                    <li class="placar">
                      <span class="placar__time placar__time--casa">{{ jogo.homeTeamName }}</span>
                      <span class="placar__numeros num" aria-hidden="true">
                        {{ jogo.homeScore }}<span aria-hidden="true">×</span>{{ jogo.awayScore }}
                      </span>
                      <span class="sr-only"
                        >&nbsp;{{ jogo.homeScore }} a {{ jogo.awayScore }}&nbsp;</span
                      >
                      <span class="placar__time">{{ jogo.awayTeamName }}</span>
                    </li>
                  }
                </ul>
              } @else if (proximos().length > 0) {
                <h4 id="cartaz-jogos" class="cartaz__subtitulo">Próximos jogos</h4>
                <ul class="placares">
                  @for (jogo of proximos(); track jogo.id) {
                    <li class="placar">
                      <span class="placar__time placar__time--casa">{{ jogo.homeTeamName }}</span>
                      <span class="placar__numeros placar__numeros--hora">
                        {{ quando(jogo.kickoffLocal) }}
                      </span>
                      <span class="placar__time">{{ jogo.awayTeamName }}</span>
                    </li>
                  }
                </ul>
              } @else {
                <h4 id="cartaz-jogos" class="cartaz__subtitulo">Partidas</h4>
                <p class="cartaz__apoio">
                  Nenhuma partida marcada ainda. O calendário aparece aqui quando a liga marcar.
                </p>
              }
            }
          }
        </section>

        <section class="painel-lideres" aria-labelledby="cartaz-ranking">
          <h4 id="cartaz-ranking" class="cartaz__subtitulo">Ranking do fantasy</h4>
          @switch (ranking().tipo) {
            @case ('carregando') {
              <app-loading label="Buscando o ranking…" skeleton="lines" />
            }
            @case ('erro') {
              <p class="cartaz__apoio">O ranking não carregou agora; ele está no campeonato.</p>
            }
            @case ('pronto') {
              @if (lideres().length === 0) {
                <p class="cartaz__apoio">
                  Ninguém pontuou ainda: o ranking começa na primeira rodada apurada.
                </p>
              } @else {
                @if (rodadaDoRanking(); as rotulo) {
                  <p class="cartaz__apoio">{{ rotulo }}</p>
                }
                <ol class="lideres">
                  @for (linha of lideres(); track linha.position + linha.displayName) {
                    <li class="lider" [class.lider--primeiro]="linha.position === 1">
                      <span class="lider__posicao num">
                        {{ linha.position }}º
                        @if (linha.tied) {
                          <span class="sr-only">, empatado</span>
                        }
                      </span>
                      <span class="lider__nome">{{ linha.displayName }}</span>
                      <span class="lider__pontos num">{{ pontos(linha.totalPoints) }}</span>
                    </li>
                  }
                </ol>
              }
            }
          }
        </section>
      </div>

      <nav class="cartaz__acoes" [attr.aria-label]="'Atalhos de ' + campeonato().name">
        <a class="acao" [routerLink]="['/c', campeonato().slug]">Abrir campeonato</a>
        <a class="acao acao--secundaria" [routerLink]="['/c', campeonato().slug, 'partidas']">
          Partidas
        </a>
        <a class="acao acao--secundaria" [routerLink]="['/c', campeonato().slug, 'ranking']">
          Ranking
        </a>
      </nav>
    </article>
  `,
  styleUrl: './landing-featured.scss',
})
export class LandingFeatured implements OnInit {
  private readonly competitions = inject(PublicCompetitionService);
  private readonly fixtures = inject(PublicFixtureService);

  readonly campeonato = input.required<PublicCompetitionSummary>();

  protected readonly calendario = signal<Carga<readonly PublicRound[]>>({ tipo: 'carregando' });
  protected readonly ranking = signal<Carga<Ranking>>({ tipo: 'carregando' });

  protected readonly modalidade = computed(() => MODALITY_LABELS[this.campeonato().modality]);

  private readonly rodadas = computed(() => {
    const atual = this.calendario();
    return atual.tipo === 'pronto' ? atual.dados : [];
  });

  protected readonly rodadasAgora = computed(() => roundsInProgress(this.rodadas()));
  protected readonly resultado = computed(() => lastPublishedRound(this.rodadas()));
  protected readonly proximos = computed(() =>
    upcomingMatches(this.rodadas(), Date.now(), PARTIDAS),
  );

  /** Só o que tem placar: partida adiada ou cancelada não entra no cartaz. */
  protected readonly placares = computed(() =>
    (this.resultado()?.matches ?? [])
      .filter((jogo) => jogo.homeScore !== null && jogo.awayScore !== null)
      .slice(0, PARTIDAS),
  );

  protected readonly lideres = computed(() => {
    const atual = this.ranking();
    return atual.tipo === 'pronto' ? atual.dados.entries.slice(0, LIDERES) : [];
  });

  /** "Depois da Rodada 3 · provisório": até quando o ranking pode mudar, em texto. */
  protected readonly rodadaDoRanking = computed(() => {
    const atual = this.ranking();
    if (atual.tipo !== 'pronto' || atual.dados.lastRoundName === null) {
      return null;
    }
    const rotulo = `Depois da ${atual.dados.lastRoundName}`;
    return atual.dados.provisional ? `${rotulo} · provisório` : rotulo;
  });

  ngOnInit(): void {
    const slug = this.campeonato().slug;
    this.fixtures.fixtures(slug).subscribe({
      next: (calendario) => this.calendario.set({ tipo: 'pronto', dados: calendario.rounds }),
      error: () => this.calendario.set({ tipo: 'erro' }),
    });
    this.competitions.ranking(slug).subscribe({
      next: (ranking) => this.ranking.set({ tipo: 'pronto', dados: ranking }),
      error: () => this.ranking.set({ tipo: 'erro' }),
    });
  }

  protected situacao(rodada: PublicRound): string {
    return ROUND_PHASE_LABELS[rodada.phase];
  }

  /** "qui., 25/09 · 20:00": o horário já vem no fuso do campeonato. */
  protected quando(kickoffLocal: string): string {
    return kickoffText(kickoffLocal);
  }

  protected pontos(valor: number): string {
    return points(valor);
  }
}
