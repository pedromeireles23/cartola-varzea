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
import { Alert, Badge, Button, Card, Dialog, FormField, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  REAL_TEAM_DUPLICATE_CODE,
  REAL_TEAM_NAME_MAX,
  REAL_TEAM_NAME_MIN,
  RealTeam,
  RealTeamService,
} from './real-team.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly times: readonly RealTeam[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Catálogo manual de times reais do campeonato (02 §9.1). */
@Component({
  selector: 'app-competition-teams',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Dialog, FormField, Loading],
  template: `
    <div class="pagina">
      <h1>Times</h1>
      <p class="intro">
        Cadastre os times reais que vão disputar o campeonato. Eles serão usados nos grupos, jogos e
        elencos.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando os times…" /></app-card>
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
          @if (proprietario() && criando()) {
            <app-card heading="Novo time">
              <form (submit)="criar($event)" novalidate>
                <app-form-field
                  label="Nome do time"
                  placeholder="União da Vila"
                  [required]="true"
                  [maxLength]="nomeMax"
                  [error]="erroDoNome()"
                  [(value)]="nome"
                />
                <div class="acoes">
                  <app-button type="submit" [loading]="salvando()">Adicionar time</app-button>
                  <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarEdicao()">
                    Cancelar
                  </app-button>
                </div>
              </form>
            </app-card>
          } @else if (proprietario()) {
            <div class="acoes">
              <app-button (pressed)="abrirNovo()">Adicionar time</app-button>
            </div>
          }

          <div class="times" role="list">
            @for (time of times(); track time.id) {
              <app-card>
                <article class="time" role="listitem" [class.time--arquivado]="time.isArchived">
                  <div class="escudo" aria-hidden="true">{{ iniciais(time.name) }}</div>
                  <div class="time__conteudo">
                    @if (editando(time.id)) {
                      <form (submit)="salvar($event, time)" novalidate>
                        <app-form-field
                          [label]="'Nome de ' + time.name"
                          [required]="true"
                          [maxLength]="nomeMax"
                          [error]="erroDoNome()"
                          [(value)]="nome"
                        />
                        <div class="acoes">
                          <app-button type="submit" [loading]="salvando()">Salvar</app-button>
                          <app-button
                            variant="ghost"
                            [disabled]="salvando()"
                            (pressed)="cancelarEdicao()"
                          >
                            Cancelar
                          </app-button>
                        </div>
                      </form>
                    } @else {
                      <div class="time__cabecalho">
                        <h2>{{ time.name }}</h2>
                        @if (time.isArchived) {
                          <app-badge tone="neutral">Arquivado</app-badge>
                        }
                      </div>
                      @if (proprietario() && !time.isArchived) {
                        <div class="acoes">
                          <app-button variant="secondary" (pressed)="abrirEdicao(time)">
                            Editar<span class="sr-only"> {{ time.name }}</span>
                          </app-button>
                          <app-button variant="ghost" (pressed)="pedirArquivamento(time)">
                            Arquivar<span class="sr-only"> {{ time.name }}</span>
                          </app-button>
                        </div>
                      }
                    }
                  </div>
                </article>
              </app-card>
            } @empty {
              <app-card>
                <p class="apoio">
                  {{
                    proprietario()
                      ? 'Nenhum time cadastrado. Adicione o primeiro participante do campeonato.'
                      : 'Nenhum time cadastrado ainda.'
                  }}
                </p>
              </app-card>
            }
          </div>
        }
      }
    </div>

    @if (proprietario()) {
      <app-dialog
        [heading]="'Arquivar ' + (arquivando()?.name ?? '') + '?'"
        [open]="arquivando() !== null"
        (dismissed)="cancelarArquivamento()"
      >
        <p>O time deixa de aparecer em novos cadastros, mas seu histórico será preservado.</p>
        @if (falhaAoArquivar()) {
          <app-alert tone="danger">{{ falhaAoArquivar() }}</app-alert>
        }
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarArquivamento()">
            Cancelar
          </app-button>
          <app-button variant="danger" [loading]="salvando()" (pressed)="arquivar()">
            Arquivar time
          </app-button>
        </div>
      </app-dialog>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-teams.scss'],
})
export class CompetitionTeamsPage {
  private readonly service = inject(RealTeamService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly nomeMax = REAL_TEAM_NAME_MAX;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly criando = signal(false);
  protected readonly editandoId = signal<string | null>(null);
  protected readonly nome = signal('');
  protected readonly erroDoNome = signal<string | undefined>(undefined);
  protected readonly salvando = signal(false);
  protected readonly arquivando = signal<RealTeam | null>(null);
  protected readonly falhaAoArquivar = signal<string | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly times = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.times : [];
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

  protected carregar(): void {
    if (this.estado().tipo !== 'pronto') {
      this.estado.set({ tipo: 'carregando' });
    }
    this.service.list(this.campeonatoId).subscribe({
      next: (times) => this.estado.set({ tipo: 'pronto', times }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrirNovo(): void {
    this.retorno.set(null);
    this.editandoId.set(null);
    this.nome.set('');
    this.erroDoNome.set(undefined);
    this.criando.set(true);
  }

  protected abrirEdicao(time: RealTeam): void {
    this.retorno.set(null);
    this.criando.set(false);
    this.nome.set(time.name);
    this.erroDoNome.set(undefined);
    this.editandoId.set(time.id);
  }

  protected editando(id: string): boolean {
    return this.editandoId() === id;
  }

  protected cancelarEdicao(): void {
    this.criando.set(false);
    this.editandoId.set(null);
    this.erroDoNome.set(undefined);
  }

  protected criar(event: Event): void {
    event.preventDefault();
    const nome = this.nomeValido();
    if (!nome) return;

    this.salvando.set(true);
    this.service.create(this.campeonatoId, nome).subscribe({
      next: (time) => this.concluir(`${time.name} adicionado.`),
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected salvar(event: Event, time: RealTeam): void {
    event.preventDefault();
    const nome = this.nomeValido();
    if (!nome) return;

    this.salvando.set(true);
    this.service.update(this.campeonatoId, time.id, nome, time.version).subscribe({
      next: (salvo) => this.concluir(`${salvo.name} salvo.`),
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected pedirArquivamento(time: RealTeam): void {
    this.falhaAoArquivar.set(null);
    this.arquivando.set(time);
  }

  protected cancelarArquivamento(): void {
    if (!this.salvando()) this.arquivando.set(null);
  }

  protected arquivar(): void {
    const time = this.arquivando();
    if (!time || this.salvando()) return;

    this.salvando.set(true);
    this.service.archive(this.campeonatoId, time.id).subscribe({
      next: () => {
        this.salvando.set(false);
        this.arquivando.set(null);
        this.concluir(`${time.name} arquivado.`);
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        if (falha.status === 404) {
          this.arquivando.set(null);
          this.concluir(`${time.name} já estava arquivado. A lista foi atualizada.`);
          return;
        }
        this.falhaAoArquivar.set(falha.message);
      },
    });
  }

  protected iniciais(nome: string): string {
    const partes = nome
      .split(/\s+/)
      .filter(Boolean)
      .filter(
        (parte) => !['da', 'de', 'do', 'das', 'dos'].includes(parte.toLocaleLowerCase('pt-BR')),
      );
    return (partes.length > 0 ? partes : [nome])
      .slice(0, 2)
      .map((parte) => parte[0])
      .join('')
      .toLocaleUpperCase('pt-BR');
  }

  private nomeValido(): string | null {
    const nome = this.nome().trim();
    if (nome.length < REAL_TEAM_NAME_MIN || nome.length > REAL_TEAM_NAME_MAX) {
      this.erroDoNome.set(`Use de ${REAL_TEAM_NAME_MIN} a ${REAL_TEAM_NAME_MAX} caracteres.`);
      return null;
    }
    this.erroDoNome.set(undefined);
    return nome;
  }

  private concluir(texto: string): void {
    this.salvando.set(false);
    this.cancelarEdicao();
    this.avisar('success', texto);
    this.carregar();
  }

  private tratarFalha(falha: ApiFailure): void {
    this.salvando.set(false);
    if (falha.code === REAL_TEAM_DUPLICATE_CODE) {
      this.erroDoNome.set('Já existe um time com esse nome neste campeonato.');
      return;
    }
    if (falha.status === 409) {
      this.cancelarEdicao();
      this.avisar(
        'warning',
        'Este time foi alterado por outra pessoa. A lista foi atualizada; refaça sua mudança.',
      );
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
