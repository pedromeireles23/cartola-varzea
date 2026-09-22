import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Dialog, FormField, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import { ImportUpload } from './import-upload';
import { ImportService } from './import.service';
import {
  CORRECTION_REASON_MAX,
  CORRECTION_REASON_MIN,
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
  imports: [Alert, Badge, Button, Card, Dialog, FormField, ImportUpload, Loading, RouterLink],
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
              @if (review()!.correction; as correction) {
                <app-alert tone="warning">
                  Rodada reaberta em {{ when(correction.reopenedAtLocal) }}.
                  @if (correction.reason) {
                    Motivo: {{ correction.reason }}
                  }
                  Quem joga continua vendo a {{ publication.revision }}ª apuração até você
                  republicar.
                </app-alert>
              } @else {
                <app-alert [tone]="publication.consolidated ? 'success' : 'info'">
                  @if (publication.consolidated) {
                    Resultado consolidado desde {{ when(publication.consolidatesAtLocal) }}.
                  } @else {
                    Resultado provisório até {{ when(publication.consolidatesAtLocal) }}: até lá,
                    uma correção de súmula ainda muda pontos e preços sem cerimônia.
                  }
                </app-alert>
              }
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
                @if (publication.correctionReason) {
                  <div class="wide">
                    <dt>Motivo da correção</dt>
                    <dd>{{ publication.correctionReason }}</dd>
                  </div>
                }
              </dl>
              @if (owner() && !correcting()) {
                <div class="actions">
                  <app-button variant="secondary" (pressed)="openReopen()">
                    Reabrir para correção
                  </app-button>
                </div>
              }
            </app-card>
          }

          @if (canPublish()) {
            <app-card [heading]="correcting() ? 'Republicar o resultado' : 'Publicar o resultado'">
              @if (correcting()) {
                <p class="support">
                  Republicar grava uma apuração nova com a súmula corrigida, sem apagar a anterior,
                  e refaz na mesma hora toda rodada seguinte que já saiu — cada uma partindo do
                  preço deixado pela anterior.
                </p>
              } @else {
                <p class="support">
                  Publicar apura a rodada inteira de uma vez: pontos de cada atleta, técnico e
                  participação, e os preços novos do mercado. O resultado nasce provisório e
                  consolida no fim da janela de correção.
                </p>
              }
              @if (owner()) {
                <div class="actions">
                  <app-button [disabled]="!review()!.ready" (pressed)="confirming.set(true)">
                    {{ correcting() ? 'Republicar resultado' : 'Publicar resultado' }}
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
            [heading]="
              (correcting() ? 'Republicar o resultado de ' : 'Publicar o resultado de ') +
              review()!.roundName +
              '?'
            "
            (dismissed)="confirming.set(false)"
          >
            @if (correcting()) {
              <p>
                A pontuação de todo mundo nesta rodada e nas seguintes que já saíram é refeita de
                uma vez. As apurações antigas continuam gravadas, e quem joga passa a ver os números
                novos com o aviso de que a rodada foi corrigida.
              </p>
            } @else {
              <p>
                Os pontos de todos os participantes e os preços novos passam a valer agora. Até o
                fim da janela de correção, o resultado aparece como provisório.
              </p>
            }
            <div dialogActions>
              <app-button variant="ghost" (pressed)="confirming.set(false)">Cancelar</app-button>
              <app-button [loading]="saving()" (pressed)="publish()">
                {{ correcting() ? 'Republicar' : 'Publicar' }}
              </app-button>
            </div>
          </app-dialog>

          <app-dialog
            [open]="reopening()"
            [heading]="'Reabrir ' + review()!.roundName + ' para correção?'"
            (dismissed)="reopening.set(false)"
          >
            <p>
              A rodada volta para conferência e a súmula aceita edição de novo. Nada muda para quem
              joga agora: os números só são trocados quando você republicar.
            </p>
            <app-form-field
              label="Motivo da correção"
              [multiline]="true"
              [rows]="3"
              [required]="reasonRequired()"
              [minLength]="reasonMin"
              [maxLength]="reasonMax"
              [error]="reasonError() ?? undefined"
              [hint]="
                reasonRequired()
                  ? 'Esta rodada já consolidou: o participante vai ler este texto.'
                  : 'A rodada ainda está provisória, então o motivo é opcional.'
              "
              [(value)]="reason"
            />
            <div dialogActions>
              <app-button variant="ghost" (pressed)="reopening.set(false)">Cancelar</app-button>
              <app-button [loading]="saving()" (pressed)="reopen()">Reabrir</app-button>
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
  protected readonly reopening = signal(false);
  protected readonly reason = signal('');
  protected readonly reasonError = signal<string | null>(null);
  protected readonly reasonMin = CORRECTION_REASON_MIN;
  protected readonly reasonMax = CORRECTION_REASON_MAX;
  protected readonly message = signal<{
    readonly tone: 'success' | 'warning' | 'danger';
    readonly text: string;
  } | null>(null);
  protected readonly review = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.review : null;
  });

  /** Súmula é lançada em andamento, em conferência e enquanto a rodada está em correção. */
  protected readonly acceptsSheets = computed(() => {
    const phase = this.review()?.phase;
    return phase === 'InProgress' || phase === 'UnderReview' || phase === 'ReopenedForCorrection';
  });

  /** A rodada foi reaberta: o que vem a seguir é republicar, não publicar. */
  protected readonly correcting = computed(() => this.review()?.correction !== null);

  protected readonly canPublish = computed(() => {
    const phase = this.review()?.phase;
    return phase === 'UnderReview' || phase === 'ReopenedForCorrection';
  });

  /** Depois de consolidada, quem joga já tomou o resultado como definitivo. */
  protected readonly reasonRequired = computed(
    () => this.review()?.publication?.consolidated ?? false,
  );

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

  protected openReopen(): void {
    this.reason.set('');
    this.reasonError.set(null);
    this.reopening.set(true);
  }

  protected reopen(): void {
    const review = this.review();
    if (!review || this.saving()) return;

    const reason = this.reason().trim();
    if (this.reasonRequired() && reason.length < CORRECTION_REASON_MIN) {
      this.reasonError.set(
        `Explique a correção em pelo menos ${CORRECTION_REASON_MIN} caracteres.`,
      );
      return;
    }

    this.saving.set(true);
    this.service
      .reopen(this.competitionId, review.roundId, review.version, reason === '' ? null : reason)
      .subscribe({
        next: (reopened) => {
          this.saving.set(false);
          this.reopening.set(false);
          this.state.set({ kind: 'ready', review: reopened });
          this.message.set({
            tone: 'warning',
            text: 'Rodada reaberta. Corrija a súmula e republique para os números mudarem.',
          });
        },
        error: (failure: ApiFailure) => {
          this.saving.set(false);
          this.reasonError.set(failure.message);
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
          text:
            published.publication!.revision > 1
              ? 'Resultado corrigido. Quem joga já vê os números novos e o motivo da correção.'
              : 'Resultado publicado. Os pontos e os preços novos já valem para todos.',
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
    return phase === 'InProgress' || phase === 'ReopenedForCorrection' ? 'warning' : 'neutral';
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
