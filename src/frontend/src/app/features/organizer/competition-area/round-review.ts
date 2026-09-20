import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Dialog, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import { ImportUpload } from './import-upload';
import { ImportService } from './import.service';
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
  imports: [Alert, Badge, Button, Card, Dialog, ImportUpload, Loading, RouterLink],
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

          @if (review()!.publication; as publication) {
            <app-card heading="Resultado publicado">
              <app-alert [tone]="publication.consolidated ? 'success' : 'info'">
                @if (publication.consolidated) {
                  Resultado consolidado desde {{ when(publication.consolidatesAtLocal) }}.
                } @else {
                  Resultado provisório até {{ when(publication.consolidatesAtLocal) }}: até lá, uma
                  correção de súmula ainda muda pontos e preços sem cerimônia.
                }
              </app-alert>
              <dl class="publication">
                <div>
                  <dt>Publicado em</dt>
                  <dd>{{ when(publication.publishedAtLocal) }}</dd>
                </div>
                <div>
                  <dt>Participações apuradas</dt>
                  <dd>{{ publication.entries }}</dd>
                </div>
                @if (publication.highestTotal !== null) {
                  <div>
                    <dt>Maior pontuação</dt>
                    <dd>{{ points(publication.highestTotal) }}</dd>
                  </div>
                  <div>
                    <dt>Média</dt>
                    <dd>{{ points(publication.averageTotal!) }}</dd>
                  </div>
                }
                <div>
                  <dt>Revisão da apuração</dt>
                  <dd>{{ publication.revision }}ª</dd>
                </div>
              </dl>
            </app-card>
          } @else if (review()!.phase === 'UnderReview') {
            <app-card heading="Publicar o resultado">
              <p class="support">
                Publicar apura a rodada inteira de uma vez: pontos de cada atleta, técnico e
                participação, e os preços novos do mercado. O resultado nasce provisório e consolida
                no fim da janela de correção.
              </p>
              @if (owner()) {
                <div class="actions">
                  <app-button [disabled]="!review()!.ready" (pressed)="confirming.set(true)">
                    Publicar resultado
                  </app-button>
                </div>
              } @else {
                <p class="support">Só quem é proprietário do campeonato publica o resultado.</p>
              }
            </app-card>
          }

          @if (acceptsSheets()) {
            <app-card heading="Súmulas por planilha">
              <p class="support">
                A planilha da rodada já vem com os jogos, o elenco de cada time e o que já foi
                lançado. Preencha, confira e importe todas as súmulas de uma vez. O placar sai dos
                gols e gols contra, e um jogo que já tem súmula é substituído: a conferência avisa
                antes.
              </p>
              <a class="acao" [href]="statisticsTemplateUrl()" download>
                Baixar planilha da rodada
              </a>
            </app-card>
            <app-import-upload
              [previewUrl]="statisticsPreviewUrl()"
              [commitUrl]="statisticsCommitUrl()"
              contagem="sumulas"
              (importado)="load(true)"
            />
          }

          <app-dialog
            [open]="confirming()"
            [heading]="'Publicar o resultado de ' + review()!.roundName + '?'"
            (dismissed)="confirming.set(false)"
          >
            <p>
              Os pontos de todos os participantes e os preços novos passam a valer agora. Até o fim
              da janela de correção, o resultado aparece como provisório.
            </p>
            <div dialogActions>
              <app-button variant="ghost" (pressed)="confirming.set(false)">Cancelar</app-button>
              <app-button [loading]="saving()" (pressed)="publish()">Publicar</app-button>
            </div>
          </app-dialog>

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
  private readonly imports = inject(ImportService);
  private readonly route = inject(ActivatedRoute);
  protected readonly context = inject(CompetitionContext);
  protected readonly owner = this.context.proprietario;
  protected readonly state = signal<State>({ kind: 'loading' });
  protected readonly saving = signal(false);
  protected readonly confirming = signal(false);
  protected readonly message = signal<{
    readonly tone: 'success' | 'warning' | 'danger';
    readonly text: string;
  } | null>(null);
  protected readonly review = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.review : null;
  });

  /** Súmula só é lançada depois do início dos jogos e antes da publicação. */
  protected readonly acceptsSheets = computed(() => {
    const phase = this.review()?.phase;
    return phase === 'InProgress' || phase === 'UnderReview';
  });

  protected readonly statisticsTemplateUrl = computed(() =>
    this.imports.statisticsTemplateUrl(this.competitionId, this.roundId),
  );

  protected readonly statisticsPreviewUrl = computed(() =>
    this.imports.statisticsPreviewUrl(this.competitionId, this.roundId),
  );

  protected readonly statisticsCommitUrl = computed(() =>
    this.imports.statisticsCommitUrl(this.competitionId, this.roundId),
  );

  constructor() {
    this.load();
  }

  protected failure(): ApiFailure | null {
    const current = this.state();
    return current.kind === 'error' ? current.failure : null;
  }

  /**
   * Com `quiet`, a revisão é atualizada sem passar pelo carregando: depois de importar,
   * a tela precisa continuar mostrando o resultado da importação.
   */
  protected load(quiet = false): void {
    if (!quiet) this.state.set({ kind: 'loading' });
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

  protected publish(): void {
    const review = this.review();
    if (!review || this.saving()) return;

    this.saving.set(true);
    this.service.publish(this.competitionId, review.roundId, review.version).subscribe({
      next: (published) => {
        this.saving.set(false);
        this.confirming.set(false);
        this.state.set({ kind: 'ready', review: published });
        this.message.set({
          tone: 'success',
          text: 'Resultado publicado. Os pontos e os preços novos já valem para todos.',
        });
      },
      error: (failure: ApiFailure) => {
        this.saving.set(false);
        this.confirming.set(false);
        this.message.set({ tone: 'danger', text: failure.message });
        this.load();
      },
    });
  }

  /** "12,50 pts", como o participante lê. */
  protected points(value: number): string {
    return `${value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} pts`;
  }

  protected phaseLabel(phase: RoundPhase): string {
    return PHASE_LABELS[phase];
  }

  protected phaseTone(phase: RoundPhase): 'success' | 'warning' | 'neutral' {
    if (phase === 'UnderReview' || phase === 'Published' || phase === 'Consolidated') {
      return 'success';
    }
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
