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
import { CalendarDays, ChevronRight } from 'lucide';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Icon, Loading, PageHeader } from '../../shared/ui';
import { points } from './fantasy-format';
import { FantasyOverview, FantasyRoundSummary, FantasyService } from './fantasy.service';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Rodadas do campeonato para quem joga (06 §4.2, `/c/:campeonato/rodadas`).
 *
 * Responde três perguntas, nesta ordem: a rodada em jogo agora (mercado aberto ou
 * fechado, com o horário absoluto), quanto a pessoa fez em cada rodada apurada, e onde
 * ver o calendário das partidas, que é público e mora na página do campeonato.
 */
@Component({
  selector: 'app-fantasy-rounds',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Icon, Loading, MarketClock, PageHeader, RouterLink],
  template: `
    <app-page-header heading="Rodadas" [subtitle]="visao()?.competitionName" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <section class="painel"><app-loading label="Abrindo as rodadas…" /></section>
      }
      @case ('erro') {
        <section class="painel">
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
          <h2 id="agora-titulo" class="painel__titulo">Agora</h2>
          <app-market-clock [market]="visao()!.market" (closed)="carregar()" />
          @if (visao()!.market.isOpen && visao()!.entry) {
            <a class="acao" [routerLink]="['/c', campeonato(), 'escalacao']">Montar time</a>
          }
        </section>

        <section class="painel painel--lista" aria-labelledby="suas-titulo">
          <h2 id="suas-titulo" class="painel__titulo">Suas rodadas</h2>
          @if (!visao()!.entry) {
            <p class="apoio">
              A pontuação de cada rodada aparece aqui depois que você entra no campeonato.
            </p>
            <a class="acao acao--secundaria" [routerLink]="['/c', campeonato(), 'jogar']">
              Entrar no campeonato
            </a>
          } @else if (rodadas().length === 0) {
            <p class="apoio">
              Nenhuma rodada apurada ainda. Quando o organizador publicar o resultado, sua pontuação
              aparece aqui.
            </p>
          } @else {
            <ul class="rodadas">
              @for (rodada of rodadas(); track rodada.roundId) {
                <li class="rodada">
                  <a
                    class="rodada__link"
                    [routerLink]="['/c', campeonato(), 'pontuacao', rodada.roundId]"
                  >
                    <span class="rodada__nome">{{ rodada.roundName }}</span>
                    <span class="rodada__pontos num">
                      {{ rodada.total === null ? 'Você não jogou' : pontos(rodada.total) }}
                    </span>
                    <svg class="rodada__seta" [appIcon]="seta" />
                  </a>
                  @if (rodada.underCorrection) {
                    <app-badge tone="warning">Em correção</app-badge>
                  } @else if (rodada.provisional) {
                    <app-badge tone="neutral">Provisória</app-badge>
                  }
                </li>
              }
            </ul>
          }
        </section>

        <a class="calendario" [routerLink]="['/c', campeonato(), 'partidas']">
          <svg [appIcon]="calendario" />
          Calendário e resultados das partidas
        </a>
      }
    }
  `,
  styleUrls: ['./fantasy.scss', './fantasy-rounds.scss'],
})
export class FantasyRoundsPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly seta = ChevronRight;
  protected readonly calendario = CalendarDays;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly rodadas = signal<readonly FantasyRoundSummary[]>([]);

  protected readonly visao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.visao : null;
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
  }
}
