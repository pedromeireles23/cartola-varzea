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
import { ChevronRight } from 'lucide';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, EmptyState, Icon, Loading, PageHeader } from '../../shared/ui';
import {
  PublicFixture,
  PublicFixtureService,
  PublicRound,
  ROUND_PHASE_LABELS,
  RoundPhase,
} from '../public/public-fixture.service';
import { kickoffText, points } from './fantasy-format';
import { FantasyOverview, FantasyRoundSummary, FantasyService } from './fantasy.service';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Fases em que a rodada está "em jogo": do mercado aberto até a súmula ir para conferência. */
const EM_JOGO: readonly RoundPhase[] = ['MarketOpen', 'MarketClosed', 'InProgress', 'UnderReview'];

/**
 * Rodadas do campeonato para quem joga (06 §4.2 e Parte 3, `/c/:campeonato/rodadas`).
 *
 * Responde três perguntas, nesta ordem: o que está em jogo agora (o mercado, com o
 * horário absoluto, e as partidas da rodada), quanto a pessoa fez em cada rodada apurada
 * e se aquele número ainda pode mudar. O calendário inteiro é público e mora na página
 * do campeonato; aqui ele é um atalho.
 */
@Component({
  selector: 'app-fantasy-rounds',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'theme-player' },
  imports: [Alert, Badge, Button, EmptyState, Icon, Loading, MarketClock, PageHeader, RouterLink],
  template: `
    <app-page-header
      heading="Rodadas"
      kicker="Competição"
      [subtitle]="visao()?.competitionName"
      variant="game"
    />

    @switch (estado().tipo) {
      @case ('carregando') {
        <section class="painel painel--corpo"><app-loading label="Abrindo as rodadas…" /></section>
      }
      @case ('erro') {
        <section class="painel painel--corpo">
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </section>
      }
      @case ('pronto') {
        @if (trilho().length > 0) {
          <nav class="trilho-wrap" aria-label="Linha do tempo das rodadas" tabindex="0">
            <ol class="trilho">
              @for (item of trilho(); track item.rodada.id) {
                <li
                  class="trilho__item"
                  [class.trilho__item--destaque]="item.destaque"
                  [attr.aria-current]="item.destaque ? 'step' : null"
                >
                  <span class="trilho__sequencia num">{{ item.rodada.sequence }}</span>
                  <span class="trilho__texto">
                    <strong>{{ item.rodada.name }}</strong>
                    <span>{{ fase(item.rodada) }}</span>
                  </span>
                  @if (pontosDaRodada(item.resumo); as totalDaRodada) {
                    <span class="trilho__pontos num">{{ totalDaRodada }}</span>
                  }
                </li>
              }
            </ol>
          </nav>
        }

        <section class="painel painel--elevado rodada-atual" aria-labelledby="agora-titulo">
          <header class="rodada-atual__topo">
            <div>
              <p class="rodada-atual__apoio">Central da rodada</p>
              <h2 id="agora-titulo" class="rodada-atual__titulo">Agora</h2>
            </div>
            <a class="painel__link" [routerLink]="['/c', campeonato(), 'partidas']">
              Calendário completo
            </a>
          </header>
          <div class="rodada-atual__acoes">
            <app-market-clock [market]="visao()!.market" (closed)="carregar()" />
            @if (chamada(); as cta) {
              <a
                [class]="cta.principal ? 'acao' : 'acao acao--secundaria'"
                [routerLink]="['/c', campeonato(), cta.caminho]"
              >
                {{ cta.texto }}
              </a>
            }
          </div>

          @if (rodadaDestaque(); as rodada) {
            <div class="jogos__cabecalho">
              <div>
                <p class="jogos__fase">{{ fase(rodada) }}</p>
                <h3 class="jogos__titulo">Partidas da {{ rodada.name }}</h3>
              </div>
              @if (rodada.underCorrection || rodada.phase === 'ReopenedForCorrection') {
                <app-badge tone="warning">Em correção</app-badge>
              } @else if (rodada.provisional) {
                <app-badge tone="neutral">Resultado provisório</app-badge>
              } @else if (rodada.phase === 'InProgress') {
                <app-badge tone="live">Em andamento</app-badge>
              }
            </div>
            <ul class="placares">
              @for (jogo of rodada.matches; track jogo.id) {
                <li class="placar-card">
                  <div class="placar-card__meta">
                    <span>{{ jogo.stageName }}</span>
                    @if (jogo.status === 'Postponed') {
                      <app-badge tone="warning">Adiada</app-badge>
                    } @else if (jogo.status === 'Cancelled') {
                      <app-badge tone="danger">Cancelada</app-badge>
                    } @else {
                      <time class="num" [attr.datetime]="jogo.kickoffAt">{{ quando(jogo) }}</time>
                    }
                  </div>
                  <div class="placar-card__confronto">
                    <strong class="placar-card__time placar-card__time--casa">
                      {{ jogo.homeTeamName }}
                    </strong>
                    <span class="placar-card__marcador num">
                      @if (temPlacar(jogo)) {
                        <strong>{{ jogo.homeScore }}</strong>
                        <span aria-hidden="true">×</span><span class="sr-only">a</span>
                        <strong>{{ jogo.awayScore }}</strong>
                      } @else {
                        <span aria-hidden="true">VS</span><span class="sr-only">contra</span>
                      }
                    </span>
                    <strong class="placar-card__time">{{ jogo.awayTeamName }}</strong>
                  </div>
                  @if (jogo.hasSheet) {
                    <a
                      class="placar-card__sumula"
                      [routerLink]="['/c', campeonato(), 'partidas', jogo.id]"
                    >
                      Ver súmula
                    </a>
                  }
                </li>
              }
            </ul>
          }
        </section>

        <section class="painel painel--elevado historico" aria-labelledby="suas-titulo">
          <header class="painel__topo">
            <div>
              <p class="historico__apoio">Desempenho no fantasy</p>
              <h2 id="suas-titulo" class="painel__titulo">Suas rodadas</h2>
            </div>
            @if (rodadas().length > 0) {
              <p class="total">
                <span>Total no campeonato&nbsp;</span>
                <strong class="total__valor num">{{ pontos(total()) }}</strong>
              </p>
            }
          </header>
          @if (!visao()!.entry) {
            <div class="painel__corpo">
              <p class="apoio">
                A pontuação de cada rodada aparece aqui depois que você entra no campeonato.
              </p>
              <a class="acao acao--secundaria" [routerLink]="['/c', campeonato(), 'jogar']">
                Entrar no campeonato
              </a>
            </div>
          } @else if (rodadas().length === 0) {
            <div class="painel__corpo">
              <app-empty-state ilustracao="placar" titulo="Nenhuma rodada apurada ainda">
                Quando o organizador publicar o resultado, sua pontuação aparece aqui.
              </app-empty-state>
            </div>
          } @else {
            <ul class="linhas">
              @for (rodada of rodadas(); track rodada.roundId) {
                <li class="rodada-item">
                  <a
                    class="linha rodada"
                    [routerLink]="['/c', campeonato(), 'pontuacao', rodada.roundId]"
                  >
                    <span class="linha__texto">
                      <span class="rodada__nome">
                        <span class="linha__principal">{{ rodada.roundName }}</span>
                        @if (rodada.underCorrection) {
                          <app-badge tone="warning">Em correção</app-badge>
                        } @else if (rodada.provisional) {
                          <app-badge tone="neutral">Provisória</app-badge>
                        }
                      </span>
                      <span class="linha__meta">{{ situacao(rodada) }}</span>
                    </span>
                    <span
                      class="rodada__pontos num"
                      [class.rodada__pontos--fora]="rodada.total === null"
                    >
                      {{ rodada.total === null ? 'Não jogou' : pontos(rodada.total) }}
                    </span>
                    <svg class="rodada__seta" [appIcon]="seta" />
                  </a>
                </li>
              }
            </ul>
          }
        </section>
      }
    }
  `,
  styleUrls: [
    '../../shared/ui/panel.scss',
    './fantasy.scss',
    './fantasy-rounds.scss',
    './fantasy-rounds-history.scss',
  ],
})
export class FantasyRoundsPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly fixtures = inject(PublicFixtureService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly seta = ChevronRight;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly rodadas = signal<readonly FantasyRoundSummary[]>([]);
  private readonly calendario = signal<readonly PublicRound[]>([]);

  protected readonly visao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.visao : null;
  });

  /**
   * A rodada em jogo: a do mercado aberto, ou a que já fechou e ainda não publicou. Com
   * mais de uma nesse intervalo, vale a mais recente, que é a que o mercado mostra.
   */
  protected readonly rodadaEmJogo = computed(() => {
    const emJogo = this.calendario().filter(
      (rodada) => EM_JOGO.includes(rodada.phase) && rodada.matches.length > 0,
    );
    return emJogo.reduce<PublicRound | null>(
      (atual, rodada) => (atual && atual.sequence > rodada.sequence ? atual : rodada),
      null,
    );
  });

  /** A rodada que ancora o placar: a ativa ou, sem uma, a última com partidas. */
  protected readonly rodadaDestaque = computed(() => {
    const ativa = this.rodadaEmJogo();
    if (ativa) return ativa;
    return this.calendario().reduce<PublicRound | null>(
      (atual, rodada) =>
        rodada.matches.length > 0 && (!atual || rodada.sequence > atual.sequence) ? rodada : atual,
      null,
    );
  });

  /** Linha do tempo completa, enriquecida apenas com a pontuação já publicada da conta. */
  protected readonly trilho = computed(() => {
    const resumos = new Map(this.rodadas().map((rodada) => [rodada.roundId, rodada]));
    const destaque = this.rodadaDestaque()?.id;
    return [...this.calendario()]
      .sort((a, b) => a.sequence - b.sequence)
      .map((rodada) => ({
        rodada,
        resumo: resumos.get(rodada.id) ?? null,
        destaque: rodada.id === destaque,
      }));
  });

  /** Soma das rodadas apuradas; é o mesmo total que a classificação usa. */
  protected readonly total = computed(() =>
    this.rodadas().reduce((soma, rodada) => soma + (rodada.total ?? 0), 0),
  );

  /**
   * A ação que a rodada permite agora, e só ela (06 §9.4): com o mercado fechado não há
   * o que fazer no elenco, então não há botão. O texto segue o do início.
   */
  protected readonly chamada = computed(() => {
    const visao = this.visao();
    if (!visao?.market.isOpen) return null;
    if (!visao.entry) {
      return { texto: 'Entrar no campeonato', caminho: 'jogar', principal: true };
    }
    if (visao.entry.slots.length === 0) {
      return { texto: 'Montar time', caminho: 'escalacao', principal: true };
    }
    return visao.entry.issues.length > 0
      ? { texto: 'Continuar escalação', caminho: 'escalacao', principal: true }
      : { texto: 'Ver escalação', caminho: 'escalacao', principal: false };
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected pontos(valor: number): string {
    return points(valor);
  }

  protected quando(jogo: PublicFixture): string {
    return kickoffText(jogo.kickoffLocal);
  }

  protected fase(rodada: PublicRound): string {
    if (rodada.underCorrection) return 'Em correção';
    return ROUND_PHASE_LABELS[rodada.phase];
  }

  protected pontosDaRodada(rodada: FantasyRoundSummary | null): string | null {
    return rodada?.total === null || rodada?.total === undefined ? null : points(rodada.total);
  }

  protected temPlacar(jogo: PublicFixture): boolean {
    return jogo.homeScore !== null && jogo.awayScore !== null;
  }

  /** Quando saiu e se ainda pode mudar, em texto: o selo sozinho não diz até quando. */
  protected situacao(rodada: FantasyRoundSummary): string {
    const publicada = `Publicada ${kickoffText(rodada.publishedAtLocal)}`;
    if (rodada.underCorrection) {
      return `${publicada} · a liga está corrigindo a súmula`;
    }
    return rodada.provisional
      ? `${publicada} · pode mudar até ${kickoffText(rodada.consolidatesAtLocal)}`
      : `${publicada} · consolidada`;
  }

  protected carregar(): void {
    this.service.overview(this.campeonato()).subscribe({
      next: (visao) => {
        this.estado.set({ tipo: 'pronto', visao });
        this.title.setTitle(`Rodadas · ${visao.competitionName}`);
        // Só quem está no campeonato tem pontuação; para os outros o servidor diria 404.
        if (visao.entry) {
          this.service.rounds(this.campeonato()).subscribe({
            next: (rodadas) => this.rodadas.set(rodadas),
            error: () => this.rodadas.set([]),
          });
        }
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
    // As partidas são complemento: se o calendário falhar, a tela continua sem elas.
    this.fixtures.fixtures(this.campeonato()).subscribe({
      next: (calendario) => this.calendario.set(calendario.rounds),
      error: () => this.calendario.set([]),
    });
  }
}
