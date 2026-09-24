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
import { Alert, BackLink, Badge, Button, Card, Loading } from '../../shared/ui';
import { PublicNav } from './public-nav';
import {
  PublicFixture,
  PublicFixtureService,
  PublicFixtures,
  PublicRound,
  ROUND_PHASE_LABELS,
} from './public-fixture.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly calendario: PublicFixtures }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Calendário público do campeonato (02 §9.1, `/c/:campeonato/partidas`).
 *
 * O placar só aparece na rodada publicada, porque é o servidor que decide isso: em
 * conferência ou em correção a súmula está sendo escrita, e o que chega aqui vem sem
 * número. A tela então diz em que pé a rodada está, em vez de deixar um traço sem
 * explicação.
 */
@Component({
  selector: 'app-competition-fixtures',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Badge, Button, Card, Loading, PublicNav, RouterLink],
  template: `
    <app-back-link [link]="['/c', campeonato()]" label="Página do campeonato" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Partidas</h1>
        <app-card><app-loading label="Montando o calendário…" /></app-card>
      }
      @case ('erro') {
        <h1>Partidas</h1>
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
        <h1>Partidas de {{ calendario()!.name }}</h1>
        <app-public-nav [campeonato]="campeonato()" atual="partidas" />

        @if (rodadas().length === 0) {
          <app-card>
            <app-alert tone="info">
              Nenhuma rodada foi criada ainda. O calendário aparece assim que a liga marcar os
              primeiros jogos.
            </app-alert>
          </app-card>
        } @else {
          @for (rodada of rodadas(); track rodada.id; let i = $index) {
            <app-card [heading]="rodada.name">
              <p class="rodada__estado">
                <app-badge [tone]="tom(rodada)">{{ situacao(rodada) }}</app-badge>
                @if (rodada.underCorrection) {
                  <span class="apoio">
                    A liga está refazendo a súmula. O placar volta quando ela republicar.
                  </span>
                }
              </p>

              <ul class="jogos">
                @for (jogo of rodada.matches; track jogo.id) {
                  <li class="jogo" [class.jogo--fora]="jogo.status !== 'Scheduled'">
                    <span class="jogo__time jogo__time--casa">{{ jogo.homeTeamName }}</span>
                    <span class="jogo__placar">
                      @if (jogo.homeScore !== null && jogo.awayScore !== null) {
                        {{ jogo.homeScore }}<span aria-hidden="true">×</span>{{ jogo.awayScore }}
                        <span class="sr-only">a</span>
                      } @else {
                        <span aria-hidden="true">×</span>
                      }
                    </span>
                    <span class="jogo__time">{{ jogo.awayTeamName }}</span>
                    <span class="jogo__detalhe">
                      {{ jogo.kickoffLocal }} · {{ jogo.stageName }}
                      @if (jogo.status !== 'Scheduled') {
                        · {{ jogo.status === 'Postponed' ? 'adiada' : 'cancelada' }}
                      }
                      @if (jogo.hasSheet) {
                        ·
                        <a [routerLink]="['/c', campeonato(), 'partidas', jogo.id]">
                          Ver súmula<span class="sr-only">
                            de {{ jogo.homeTeamName }} contra {{ jogo.awayTeamName }}</span
                          >
                        </a>
                      }
                    </span>
                  </li>
                } @empty {
                  <li class="apoio">Nenhum jogo marcado nesta rodada.</li>
                }
              </ul>
            </app-card>
          }
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './fixtures.scss'],
})
export class CompetitionFixturesPage implements OnInit {
  private readonly service = inject(PublicFixtureService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly calendario = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.calendario : null;
  });

  protected readonly rodadas = computed(() => this.calendario()?.rounds ?? []);

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  /** A fase em pt-BR; em correção o rótulo da fase já diz o que está acontecendo. */
  protected situacao(rodada: PublicRound): string {
    return ROUND_PHASE_LABELS[rodada.phase];
  }

  protected tom(rodada: PublicRound): 'neutral' | 'brand' | 'success' | 'warning' {
    if (rodada.underCorrection) {
      return 'warning';
    }

    if (rodada.phase === 'Consolidated') {
      return 'success';
    }

    return rodada.resultPublished || rodada.phase === 'MarketOpen' ? 'brand' : 'neutral';
  }

  protected jogos(rodada: PublicRound): readonly PublicFixture[] {
    return rodada.matches;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.fixtures(this.campeonato()).subscribe({
      next: (calendario) => {
        this.estado.set({ tipo: 'pronto', calendario });
        this.meta.set({
          title: `Partidas de ${calendario.name}`,
          description: `Calendário de ${calendario.name}: rodadas, horários e placares das partidas já publicadas.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
