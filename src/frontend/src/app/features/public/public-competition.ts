import { DatePipe, DecimalPipe } from '@angular/common';
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
import { Alert, BackLink, Badge, Card, FailureState, Loading } from '../../shared/ui';
import { formationText, timeZoneLabel } from '../organizer/competition-area/competition-format';
import { FORMAT_LABELS } from '../organizer/competition-area/stage.service';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { PublicCompetition, PublicCompetitionService } from './public-competition.service';
import { PublicFixtureService, PublicRound, ROUND_PHASE_LABELS } from './public-fixture.service';
import { PublicNav } from './public-nav';
import { lastPublishedRound, roundsInProgress, upcomingMatches } from './public-rounds';
import { kickoffText, teamInitials } from '../fantasy/fantasy-format';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonato: PublicCompetition }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Página pública do campeonato (02 §9.1, `/c/:campeonato`).
 *
 * O endereço é o slug, dado na publicação e estável a partir dali. Esta versão mostra
 * o que já existe — regras da modalidade, fases com times e o catálogo — e o convite
 * para jogar, que pede a conta antes de entrar. O ranking geral saiu na Fase 11 e tem
 * página própria; rodadas e tabela entram com a Fase 8.
 */
@Component({
  selector: 'app-public-competition',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'theme-player' },
  imports: [
    Alert,
    BackLink,
    Badge,
    Card,
    DatePipe,
    DecimalPipe,
    FailureState,
    Loading,
    PublicNav,
    RouterLink,
  ],
  template: `
    <app-back-link link="/campeonatos" label="Todos os campeonatos" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Abrindo o campeonato…" skeleton="cards" /></app-card>
      }
      @case ('erro') {
        <h1>Campeonato</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-failure-state [failure]="falha()!" (retry)="carregar()" />
          }
        </app-card>
      }
      @case ('pronto') {
        <header class="capa-campeonato tema-destaque">
          <p class="capa-campeonato__kicker">
            {{ modalidade() }} · Temporada {{ dados()!.season }}
          </p>
          <h1>{{ dados()!.name }}</h1>
          <p class="intro">{{ dados()!.organizationName }}</p>

          <!-- O que está em jogo agora fica na capa, junto das ações que ele permite. -->
          @if (rodadasAgora().length > 0) {
            <section class="capa-campeonato__agora" aria-labelledby="agora-titulo">
              <h2 id="agora-titulo" class="agora__rotulo">Agora</h2>
              @for (rodada of rodadasAgora(); track rodada.id) {
                <p class="agora">
                  <strong>{{ rodada.name }}</strong>
                  <app-badge [tone]="rodada.underCorrection ? 'warning' : 'brand'">
                    {{ situacao(rodada) }}
                  </app-badge>
                </p>
                @if (rodada.underCorrection) {
                  <p class="apoio">
                    A liga está refazendo a súmula desta rodada. Os números voltam quando ela
                    republicar.
                  </p>
                }
              }
            </section>
          }

          <p class="acoes-publicas">
            @if (!semCatalogo()) {
              <a class="jogar" [routerLink]="['/c', dados()!.slug, 'jogar']"
                >Jogar neste campeonato</a
              >
            }
            <a class="acao" [routerLink]="['/c', dados()!.slug, 'ranking']">Ver o ranking</a>
          </p>
        </header>
        <app-public-nav [campeonato]="dados()!.slug" atual="visao" />

        @if (emMontagem()) {
          <app-card heading="Campeonato ainda em montagem">
            <app-alert tone="info">
              A liga publicou este campeonato, mas ainda não cadastrou os times nem as fases.
            </app-alert>
            <p>
              Quando isso acontecer, o elenco de cada time, a tabela e as rodadas aparecem aqui — e
              o campeonato abre para quem quiser montar uma equipe.
            </p>
            <a class="acao acao--secundaria" routerLink="/campeonatos">Ver outros campeonatos</a>
          </app-card>
        }

        @if (proximosJogos().length > 0) {
          <app-card heading="Próximos jogos">
            <ul class="jogos">
              @for (jogo of proximosJogos(); track jogo.id) {
                <li class="jogo">
                  <span class="jogo__time jogo__time--casa">{{ jogo.homeTeamName }}</span>
                  <span class="jogo__placar" aria-hidden="true"
                    ><span aria-hidden="true">×</span></span
                  >
                  <span class="sr-only">&nbsp;contra&nbsp;</span>
                  <span class="jogo__time">{{ jogo.awayTeamName }}</span>
                  <span class="jogo__detalhe"
                    ><span class="sr-only">,&nbsp;</span>{{ quando(jogo.kickoffLocal) }} ·
                    {{ jogo.stageName }}</span
                  >
                </li>
              }
            </ul>
            <a class="acao acao--secundaria" [routerLink]="['/c', dados()!.slug, 'partidas']">
              Ver todas as partidas
            </a>
          </app-card>
        } @else if (ultimoResultado(); as rodada) {
          <app-card [heading]="'Último resultado · ' + rodada.name">
            <ul class="jogos">
              @for (jogo of rodada.matches; track jogo.id) {
                <li class="jogo">
                  <span class="jogo__time jogo__time--casa">{{ jogo.homeTeamName }}</span>
                  <span class="jogo__placar" aria-hidden="true">
                    {{ jogo.homeScore }}<span aria-hidden="true">×</span>{{ jogo.awayScore }}
                  </span>
                  <span class="sr-only"
                    >&nbsp;{{ jogo.homeScore }} a {{ jogo.awayScore }}&nbsp;</span
                  >
                  <span class="jogo__time">{{ jogo.awayTeamName }}</span>
                  <span class="jogo__detalhe"
                    ><span class="sr-only">,&nbsp;</span>{{ quando(jogo.kickoffLocal) }} ·
                    {{ jogo.stageName }}</span
                  >
                </li>
              }
            </ul>
            <a class="acao acao--secundaria" [routerLink]="['/c', dados()!.slug, 'partidas']">
              Ver todas as partidas
            </a>
          </app-card>
        }

        <app-card heading="Como se joga">
          <dl class="dados">
            <div class="dados__item">
              <dt>Titulares</dt>
              <dd>{{ dados()!.modalityProfile.starters }}: {{ formacao() }}</dd>
            </div>
            <div class="dados__item">
              <dt>Banco</dt>
              <dd>{{ dados()!.modalityProfile.benchSize }} reservas, um por posição</dd>
            </div>
            <div class="dados__item">
              <dt>Elenco</dt>
              <dd>{{ dados()!.modalityProfile.squadAthletes }} atletas e 1 técnico</dd>
            </div>
            <div class="dados__item">
              <dt>Orçamento inicial</dt>
              <dd>{{ dados()!.modalityProfile.budget | number: '1.0-2' }} créditos</dd>
            </div>
            <div class="dados__item">
              <dt>Fuso horário</dt>
              <dd>{{ fuso() }}</dd>
            </div>
            <div class="dados__item">
              <dt>No ar desde</dt>
              <dd>{{ dados()!.publishedAt | date: 'dd/MM/yyyy' }}</dd>
            </div>
          </dl>
        </app-card>

        @if (!emMontagem()) {
          <app-card heading="Fases">
            @for (fase of dados()!.stages; track fase.sequence) {
              <section class="fase">
                <h3 class="fase__titulo">{{ fase.sequence }}. {{ fase.name }}</h3>
                <p class="apoio">{{ formato(fase.format) }}</p>

                @if (fase.groups.length > 0) {
                  <ul class="grupos">
                    @for (grupo of fase.groups; track grupo.name) {
                      <li class="grupo">
                        <span class="grupo__nome">{{ grupo.name }}</span>
                        <span>{{ grupo.teams.join(', ') }}</span>
                      </li>
                    }
                  </ul>
                } @else if (fase.teams.length > 0) {
                  <p>{{ fase.teams.join(', ') }}</p>
                } @else {
                  <p class="apoio">Times ainda não confirmados nesta fase.</p>
                }
              </section>
            } @empty {
              <p class="apoio">
                As fases ainda não foram publicadas. Elas dizem quem joga contra quem.
              </p>
            }
          </app-card>

          <app-card heading="Times">
            <ul class="times">
              @for (time of dados()!.teams; track time.id) {
                <li class="time">
                  <span class="time__escudo" aria-hidden="true">{{ iniciais(time.name) }}</span>
                  <a [routerLink]="['/c', dados()!.slug, 'times', time.id]">{{ time.name }}</a>
                  <span class="time__elenco">{{ elenco(time.athletes) }}</span>
                </li>
              } @empty {
                <li class="apoio">
                  O catálogo de times ainda não foi cadastrado, então não dá para montar uma equipe
                  por enquanto.
                </li>
              }
            </ul>
          </app-card>
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './fixtures.scss'],
})
export class PublicCompetitionPage implements OnInit {
  private readonly service = inject(PublicCompetitionService);
  private readonly calendario = inject(PublicFixtureService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  /**
   * O calendário é acessório aqui: se ele falhar, a página do campeonato continua de pé
   * sem a régua de "agora" e sem os próximos jogos.
   */
  protected readonly rodadas = signal<readonly PublicRound[]>([]);

  protected readonly dados = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonato : null;
  });

  protected readonly modalidade = computed(() => {
    const atual = this.dados();
    return atual ? MODALITY_LABELS[atual.modality] : '';
  });

  protected readonly formacao = computed(() => {
    const atual = this.dados();
    return atual ? formationText(atual.modalityProfile) : '';
  });

  protected readonly fuso = computed(() => timeZoneLabel(this.dados()?.timeZoneId ?? ''));

  /**
   * Sem time no catálogo não há atleta para comprar, então o convite para jogar levaria
   * a um beco sem saída. A página diz o que falta em vez de oferecer o que não dá.
   */
  protected readonly semCatalogo = computed(() => (this.dados()?.teams.length ?? 0) === 0);

  /**
   * Publicado, mas ainda sem nada dentro. Três "nada aqui" separados — fases, times e
   * catálogo — não explicam o que está acontecendo; um aviso só explica.
   */
  protected readonly emMontagem = computed(
    () => this.semCatalogo() && (this.dados()?.stages.length ?? 0) === 0,
  );

  /** As rodadas que ainda não fecharam resultado; ver `roundsInProgress`. */
  protected readonly rodadasAgora = computed(() => roundsInProgress(this.rodadas()));

  /** O que vem por aí, no relógio de quem está lendo, no máximo três. */
  protected readonly proximosJogos = computed(() => upcomingMatches(this.rodadas(), Date.now(), 3));

  /** A última rodada com resultado no ar; some enquanto ela está em correção. */
  protected readonly ultimoResultado = computed(() => lastPublishedRound(this.rodadas()));

  ngOnInit(): void {
    this.carregar();
  }

  /** "ter., 29/09 · 09:00": o horário já vem no fuso do campeonato. */
  protected quando(kickoffLocal: string): string {
    return kickoffText(kickoffLocal);
  }

  /** "BN" para Bar do Nico FC: o escudo do time, sem imagem (07 V7). */
  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected situacao(rodada: PublicRound): string {
    return ROUND_PHASE_LABELS[rodada.phase];
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.get(this.campeonato()).subscribe({
      next: (campeonato) => {
        this.estado.set({ tipo: 'pronto', campeonato });
        this.meta.set({
          title: campeonato.name,
          description: `${MODALITY_LABELS[campeonato.modality]} · temporada ${campeonato.season}, por ${campeonato.organizationName}. Times, fases e classificação do campeonato, com o fantasy aberto a quem quiser jogar.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });

    this.calendario.fixtures(this.campeonato()).subscribe({
      next: (calendario) => this.rodadas.set(calendario.rounds),
      error: () => this.rodadas.set([]),
    });
  }

  protected formato(format: PublicCompetition['stages'][number]['format']): string {
    return FORMAT_LABELS[format];
  }

  protected elenco(atletas: number): string {
    return `${atletas} ${atletas === 1 ? 'atleta inscrito' : 'atletas inscritos'}`;
  }
}
