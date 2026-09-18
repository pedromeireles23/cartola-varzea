import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  MATCH_STATUS_LABELS,
  PHASE_LABELS,
  RoundPhase,
  RoundReview,
  RoundService,
} from './round.service';

type State =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly review: RoundReview }
  | { readonly kind: 'error'; readonly failure: ApiFailure };

const EVENT_LABELS: Readonly<Record<string, string>> = {
  Goal: 'Gols',
  Assist: 'Assistências',
  GoalkeeperSave: 'Defesas',
  PenaltySave: 'Pênaltis defendidos',
  YellowCard: 'Amarelos',
  RedCard: 'Vermelhos',
  OwnGoal: 'Gols contra',
  PenaltyMiss: 'Pênaltis perdidos',
};

@Component({
  selector: 'app-round-review',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading, RouterLink],
  template: `
    <div class="pagina">
      <a class="voltar" routerLink="../..">← Voltar para rodadas</a>

      @switch (state().kind) {
        @case ('loading') {
          <app-card><app-loading label="Consolidando as súmulas…" /></app-card>
        }
        @case ('error') {
          <app-card>
            <app-alert tone="danger">{{ failure()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="load()">Tentar de novo</app-button>
          </app-card>
        }
        @case ('ready') {
          <div class="cabecalho">
            <div>
              <p class="eyebrow">Revisão consolidada</p>
              <h1>{{ review()!.roundName }}</h1>
            </div>
            <app-badge [tone]="phaseTone(review()!.phase)">
              {{ phaseLabel(review()!.phase) }}
            </app-badge>
          </div>

          @if (message(); as notice) {
            <app-alert [tone]="notice.tone">{{ notice.text }}</app-alert>
          }

          <app-card heading="Prontidão da rodada">
            <p class="summary">
              <strong>{{ review()!.completedSheets }} de {{ review()!.scheduledMatches }}</strong>
              súmulas obrigatórias preenchidas
            </p>
            @if (review()!.ready) {
              <app-alert tone="success">
                Todos os jogos marcados começaram e suas súmulas estão consistentes.
              </app-alert>
            } @else {
              <app-alert tone="warning">
                Resolva todas as pendências abaixo antes de enviar a rodada para revisão.
              </app-alert>
              <ul class="pending">
                @for (
                  item of review()!.pending;
                  track item.code + '-' + item.matchId + '-' + item.message
                ) {
                  <li>
                    {{ item.message }}
                    @if (item.matchId) {
                      <a [routerLink]="['../../../partidas', item.matchId, 'sumula']"
                        >Abrir súmula</a
                      >
                    }
                  </li>
                }
              </ul>
            }

            @if (owner() && review()!.phase === 'InProgress') {
              <div class="actions">
                <app-button [disabled]="!review()!.ready" [loading]="saving()" (pressed)="submit()">
                  Enviar para revisão
                </app-button>
              </div>
            }
          </app-card>

          <h2>Partidas</h2>
          <div class="matches">
            @for (match of review()!.matches; track match.matchId) {
              <app-card [heading]="match.homeTeamName + ' × ' + match.awayTeamName">
                <div class="match-heading">
                  <span>{{ when(match.kickoffLocal) }}</span>
                  <app-badge [tone]="match.hasSheet ? 'success' : 'neutral'">
                    {{
                      match.requiresSheet
                        ? match.hasSheet
                          ? 'Súmula preenchida'
                          : 'Súmula pendente'
                        : statusLabel(match.status)
                    }}
                  </app-badge>
                </div>
                @if (match.hasSheet) {
                  <p class="score">{{ match.homeScore }} × {{ match.awayScore }}</p>
                  <p class="participants">{{ match.participants }} participante(s)</p>
                  @if (match.events.length > 0) {
                    <ul class="events">
                      @for (event of match.events; track event.type) {
                        <li>
                          <strong>{{ event.quantity }}</strong> {{ eventLabel(event.type) }}
                        </li>
                      }
                    </ul>
                  } @else {
                    <p class="support">Nenhum evento objetivo registrado.</p>
                  }
                }
                @if (match.requiresSheet) {
                  <a class="action" [routerLink]="['../../../partidas', match.matchId, 'sumula']">
                    {{ match.hasSheet ? 'Conferir súmula' : 'Preencher súmula' }}
                  </a>
                }
              </app-card>
            }
          </div>
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './round-review.scss'],
})
export class RoundReviewPage {
  private readonly service = inject(RoundService);
  private readonly route = inject(ActivatedRoute);
  protected readonly context = inject(CompetitionContext);
  protected readonly owner = this.context.proprietario;
  protected readonly state = signal<State>({ kind: 'loading' });
  protected readonly saving = signal(false);
  protected readonly message = signal<{
    readonly tone: 'success' | 'warning' | 'danger';
    readonly text: string;
  } | null>(null);
  protected readonly review = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.review : null;
  });

  constructor() {
    this.load();
  }

  protected failure(): ApiFailure | null {
    const current = this.state();
    return current.kind === 'error' ? current.failure : null;
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });
    this.service.review(this.competitionId, this.roundId).subscribe({
      next: (review) => this.state.set({ kind: 'ready', review }),
      error: (failure: ApiFailure) => this.state.set({ kind: 'error', failure }),
    });
  }

  protected submit(): void {
    const review = this.review();
    if (!review?.ready || this.saving()) return;

    this.saving.set(true);
    this.service
      .changeStatus(this.competitionId, review.roundId, 'SendToReview', review.version)
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set({ tone: 'success', text: 'Rodada enviada para revisão.' });
          this.load();
        },
        error: (failure: ApiFailure) => {
          this.saving.set(false);
          this.message.set({ tone: 'danger', text: failure.message });
          this.load();
        },
      });
  }

  protected phaseLabel(phase: RoundPhase): string {
    return PHASE_LABELS[phase];
  }

  protected phaseTone(phase: RoundPhase): 'success' | 'warning' | 'neutral' {
    if (phase === 'UnderReview') return 'success';
    return phase === 'InProgress' ? 'warning' : 'neutral';
  }

  protected statusLabel(status: keyof typeof MATCH_STATUS_LABELS): string {
    return MATCH_STATUS_LABELS[status];
  }

  protected eventLabel(type: string): string {
    return EVENT_LABELS[type] ?? type;
  }

  protected when(local: string): string {
    const [date, time] = local.split('T');
    const [year, month, day] = date.split('-');
    return `${day}/${month}/${year} ${time}`;
  }

  private get competitionId(): string {
    return this.context.campeonato()?.id ?? '';
  }

  private get roundId(): string {
    return this.route.snapshot.paramMap.get('rodada') ?? '';
  }
}
