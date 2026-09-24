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
import { Alert, Badge, Button, Icon, Loading, PageHeader } from '../../shared/ui';
import {
  PublicFixture,
  PublicFixtureService,
  PublicRound,
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
  imports: [Alert, Badge, Button, Icon, Loading, MarketClock, PageHeader, RouterLink],
  template: `
    <app-page-header heading="Rodadas" [subtitle]="visao()?.competitionName" />

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
        <section class="painel" aria-labelledby="agora-titulo">
          <header class="painel__topo">
            <h2 id="agora-titulo" class="painel__titulo">Agora</h2>
            <a class="painel__link" [routerLink]="['/c', campeonato(), 'partidas']">
              Calendário completo
            </a>
          </header>
          <div class="painel__corpo agora">
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

          @if (rodadaEmJogo(); as rodada) {
            <h3 class="jogos__titulo">Partidas da {{ rodada.name }}</h3>
            <ul class="linhas">
              @for (jogo of rodada.matches; track jogo.id) {
                <li class="linha">
                  <span class="linha__texto">
                    <span class="linha__principal">
                      {{ jogo.homeTeamName }} <span aria-hidden="true">×</span
                      ><span class="sr-only">contra</span> {{ jogo.awayTeamName }}
                    </span>
                    <span class="linha__meta">{{ jogo.stageName }}</span>
                  </span>
                  @if (jogo.status === 'Postponed') {
                    <app-badge tone="warning">Adiada</app-badge>
                  } @else if (jogo.status === 'Cancelled') {
                    <app-badge tone="danger">Cancelada</app-badge>
                  } @else {
                    <span class="linha__meta linha__meta--fixa num">{{ quando(jogo) }}</span>
                  }
                </li>
              }
            </ul>
          }
        </section>

        <section class="painel" aria-labelledby="suas-titulo">
          <header class="painel__topo">
            <h2 id="suas-titulo" class="painel__titulo">Suas rodadas</h2>
            @if (rodadas().length > 0) {
              <p class="painel__apoio">
                Total no campeonato <strong class="total num">{{ pontos(total()) }}</strong>
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
            <p class="painel__corpo apoio">
              Nenhuma rodada apurada ainda. Quando o organizador publicar o resultado, sua pontuação
              aparece aqui.
            </p>
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
  styleUrls: ['../../shared/ui/panel.scss', './fantasy.scss', './fantasy-rounds.scss'],
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
