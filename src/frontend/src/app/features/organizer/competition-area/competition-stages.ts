import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Dialog, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import { StageForm } from './stage-form';
import {
  FORMAT_LABELS,
  MAX_STAGES,
  STAGE_LIMIT_CODE,
  Stage,
  StageInput,
  StageService,
  TIEBREAK_LABELS,
} from './stage.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly fases: readonly Stage[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Qual formulário está aberto: nenhum, o de nova fase ou o de uma fase existente. */
type Edicao = { readonly tipo: 'nova' } | { readonly tipo: 'fase'; readonly id: string } | null;

/**
 * Fases do campeonato (02 §9.1, `/organizar/c/:campeonato/fases`).
 *
 * Proprietário cria, edita, reordena e remove; auxiliar só lê. A ordem muda por botões,
 * sem arrastar. Quando outra pessoa mexeu nas fases, a lista é recarregada com aviso em
 * vez de sobrescrever.
 */
@Component({
  selector: 'app-competition-stages',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Dialog, Loading, StageForm],
  template: `
    <div class="pagina">
      <h1>Fases</h1>
      <p class="intro">
        Organize as fases na ordem em que acontecem. Na várzea o mata-mata costuma ser definido
        depois dos grupos: dá para acrescentar e reordenar a qualquer momento.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card>
            <app-loading label="Buscando as fases…" />
          </app-card>
        }

        @case ('erro') {
          <app-card>
            <app-alert tone="danger">
              <p>{{ falha()!.message }}</p>
              @if (falha()!.traceId) {
                <p class="trace">Código de rastreio: {{ falha()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          </app-card>
        }

        @case ('pronto') {
          @for (fase of fases(); track fase.id; let primeira = $first, ultima = $last) {
            <app-card [heading]="fase.sequence + '. ' + fase.name">
              @if (editandoFase(fase.id)) {
                <app-stage-form
                  submitLabel="Salvar fase"
                  [initial]="fase"
                  [saving]="salvando()"
                  (submitted)="salvar(fase, $event)"
                  (cancelled)="fecharFormulario()"
                />
              } @else {
                <dl class="dados">
                  <div class="dados__item">
                    <dt>Formato</dt>
                    <dd>
                      <app-badge [tone]="fase.format === 'Groups' ? 'brand' : 'neutral'">
                        {{ formato(fase) }}
                      </app-badge>
                    </dd>
                  </div>
                  @if (fase.format === 'Groups') {
                    <div class="dados__item">
                      <dt>Grupos</dt>
                      <dd>{{ nomesDosGrupos(fase) }}</dd>
                    </div>
                    <div class="dados__item">
                      <dt>Desempate</dt>
                      <dd>
                        <ol class="desempate">
                          @for (criterio of fase.tiebreakers; track criterio) {
                            <li>{{ criterioLegivel(criterio) }}</li>
                          }
                        </ol>
                      </dd>
                    </div>
                  }
                </dl>

                @if (proprietario()) {
                  <div class="acoes">
                    <app-button variant="secondary" (pressed)="abrirEdicao(fase)">
                      Editar<span class="sr-only"> {{ fase.name }}</span>
                    </app-button>
                    <app-button
                      variant="ghost"
                      [disabled]="primeira || ocupado()"
                      (pressed)="mover(fase, -1)"
                    >
                      Subir<span class="sr-only"> {{ fase.name }}</span>
                    </app-button>
                    <app-button
                      variant="ghost"
                      [disabled]="ultima || ocupado()"
                      (pressed)="mover(fase, 1)"
                    >
                      Descer<span class="sr-only"> {{ fase.name }}</span>
                    </app-button>
                    <app-button variant="ghost" (pressed)="pedirRemocao(fase)">
                      Remover<span class="sr-only"> {{ fase.name }}</span>
                    </app-button>
                  </div>
                }
              }
            </app-card>
          } @empty {
            <app-card>
              @if (proprietario()) {
                <p class="apoio">
                  Nenhuma fase ainda. Comece pela fase de grupos ou pelo mata-mata, conforme o
                  regulamento.
                </p>
              } @else {
                <p class="apoio">Nenhuma fase cadastrada ainda.</p>
              }
            </app-card>
          }

          @if (proprietario()) {
            @if (edicao()?.tipo === 'nova') {
              <app-card heading="Nova fase">
                <app-stage-form
                  submitLabel="Adicionar fase"
                  [saving]="salvando()"
                  (submitted)="criar($event)"
                  (cancelled)="fecharFormulario()"
                />
              </app-card>
            } @else if (fases().length >= limite) {
              <p class="apoio">O campeonato chegou ao limite de {{ limite }} fases.</p>
            } @else {
              <div class="acoes">
                <app-button [disabled]="edicao() !== null" (pressed)="abrirNova()">
                  Adicionar fase
                </app-button>
              </div>
            }
          }
        }
      }
    </div>

    @if (proprietario()) {
      <app-dialog
        [heading]="'Remover ' + (removendo()?.name ?? '') + '?'"
        [open]="removendo() !== null"
        (dismissed)="cancelarRemocao()"
      >
        <p>As fases seguintes sobem uma posição. Os grupos desta fase deixam de existir.</p>
        @if (falhaAoRemover()) {
          <app-alert tone="danger">{{ falhaAoRemover() }}</app-alert>
        }
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="ocupado()" (pressed)="cancelarRemocao()">
            Cancelar
          </app-button>
          <app-button variant="danger" [loading]="ocupado()" (pressed)="remover()">
            Remover fase
          </app-button>
        </div>
      </app-dialog>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss'],
})
export class CompetitionStagesPage {
  private readonly service = inject(StageService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly limite = MAX_STAGES;

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly edicao = signal<Edicao>(null);
  protected readonly salvando = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly removendo = signal<Stage | null>(null);
  protected readonly falhaAoRemover = signal<string | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly fases = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.fases : [];
  });

  constructor() {
    this.carregar();
  }

  private get campeonatoId(): string {
    return this.contexto.campeonato()?.id ?? '';
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected formato(fase: Stage): string {
    return FORMAT_LABELS[fase.format];
  }

  protected criterioLegivel(criterio: Stage['tiebreakers'][number]): string {
    return TIEBREAK_LABELS[criterio];
  }

  protected nomesDosGrupos(fase: Stage): string {
    return fase.groups.map((grupo) => grupo.name).join(', ');
  }

  protected editandoFase(id: string): boolean {
    const atual = this.edicao();
    return atual?.tipo === 'fase' && atual.id === id;
  }

  /** Recarrega sem voltar ao estado de carregamento quando a lista já está na tela. */
  protected carregar(): void {
    if (this.estado().tipo !== 'pronto') {
      this.estado.set({ tipo: 'carregando' });
    }

    this.service.list(this.campeonatoId).subscribe({
      next: (fases) => this.estado.set({ tipo: 'pronto', fases }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrirNova(): void {
    this.retorno.set(null);
    this.edicao.set({ tipo: 'nova' });
  }

  protected abrirEdicao(fase: Stage): void {
    this.retorno.set(null);
    this.edicao.set({ tipo: 'fase', id: fase.id });
  }

  protected fecharFormulario(): void {
    this.edicao.set(null);
  }

  protected criar(entrada: StageInput): void {
    this.salvando.set(true);
    this.service.create(this.campeonatoId, entrada).subscribe({
      next: (fase) => this.concluir(`${fase.name} adicionada.`),
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected salvar(fase: Stage, entrada: StageInput): void {
    this.salvando.set(true);
    this.service.update(this.campeonatoId, fase.id, entrada, fase.version).subscribe({
      next: (salva) => this.concluir(`${salva.name} salva.`),
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected mover(fase: Stage, direcao: -1 | 1): void {
    const ordem = this.fases().map((item) => item.id);
    const origem = ordem.indexOf(fase.id);
    const destino = origem + direcao;
    if (origem < 0 || destino < 0 || destino >= ordem.length || this.ocupado()) {
      return;
    }

    [ordem[origem], ordem[destino]] = [ordem[destino], ordem[origem]];
    this.ocupado.set(true);
    this.retorno.set(null);
    this.service.reorder(this.campeonatoId, ordem).subscribe({
      next: (fases) => {
        this.ocupado.set(false);
        this.estado.set({ tipo: 'pronto', fases });
        this.avisar('success', `${fase.name} agora é a fase ${destino + 1}.`);
      },
      error: (falha: ApiFailure) => {
        this.ocupado.set(false);
        this.tratarFalha(falha);
      },
    });
  }

  protected pedirRemocao(fase: Stage): void {
    this.falhaAoRemover.set(null);
    this.removendo.set(fase);
  }

  protected cancelarRemocao(): void {
    if (!this.ocupado()) {
      this.removendo.set(null);
    }
  }

  protected remover(): void {
    const fase = this.removendo();
    if (!fase || this.ocupado()) {
      return;
    }

    this.ocupado.set(true);
    this.service.remove(this.campeonatoId, fase.id).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.removendo.set(null);
        this.concluir(`${fase.name} removida.`);
      },
      error: (falha: ApiFailure) => {
        this.ocupado.set(false);
        if (falha.status === 404) {
          this.removendo.set(null);
          this.concluir(`${fase.name} já tinha sido removida. A lista foi atualizada.`);
          return;
        }

        this.falhaAoRemover.set(falha.message);
      },
    });
  }

  private concluir(texto: string): void {
    this.salvando.set(false);
    this.edicao.set(null);
    this.avisar('success', texto);
    this.carregar();
  }

  private tratarFalha(falha: ApiFailure): void {
    this.salvando.set(false);
    if (falha.status === 409 && falha.code !== STAGE_LIMIT_CODE) {
      // Outra aba ou outra pessoa mexeu nas fases: a lista atual vale mais que a edição.
      this.edicao.set(null);
      this.avisar(
        'warning',
        'As fases foram alteradas por outra pessoa enquanto você editava. A lista foi atualizada; refaça sua mudança.',
      );
      this.carregar();
      return;
    }

    if (falha.code === STAGE_LIMIT_CODE) {
      this.edicao.set(null);
      this.avisar('warning', `O campeonato chegou ao limite de ${MAX_STAGES} fases.`);
      this.carregar();
      return;
    }

    this.avisar('danger', falha.message);
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
