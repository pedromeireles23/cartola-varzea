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
import { RouterLink } from '@angular/router';
import { Observable, catchError, concatMap, forkJoin, of, throwError } from 'rxjs';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Alert, Button, Card, Dialog } from '../../../../shared/ui';
import {
  ATHLETE_NAME_MAX,
  ATHLETE_NAME_MIN,
  Athlete,
  AthletePosition,
  AthleteService,
  PriceTier,
} from '../athlete.service';
import { ImportRow, ImportService, importIssues, rowIndexOf } from '../import.service';
import { RealTeam } from '../real-team.service';
import { SetupFooter } from './competition-setup';
import { parseRosterList } from './roster-list';
import { SetupStore } from './setup-store';
import { teamInitials } from './setup-text';

interface LinhaAtleta {
  readonly chave: number;
  readonly nome: string;
  readonly posicao: AthletePosition | '';
  readonly nivel: PriceTier;
}

interface Edicao {
  readonly nome: string;
  readonly posicao: AthletePosition;
  readonly nivel: PriceTier;
}

interface ErroDaLinha {
  readonly nome?: string;
  readonly posicao?: string;
}

const POSICOES: readonly { readonly value: AthletePosition; readonly label: string }[] = [
  { value: 'Goalkeeper', label: 'Goleiro' },
  { value: 'Defender', label: 'Defensor' },
  { value: 'Midfielder', label: 'Meio-campista' },
  { value: 'Forward', label: 'Atacante' },
];

const NIVEIS: readonly { readonly value: PriceTier; readonly label: string }[] = [
  { value: 'Star', label: 'Destaque' },
  { value: 'Regular', label: 'Regular' },
  { value: 'Basic', label: 'Básico' },
];

/** Os valores que a importação aceita, como a coluna da planilha os escreve. */
const POSICAO_NA_PLANILHA: Readonly<Record<AthletePosition, string>> = {
  Goalkeeper: 'goleiro',
  Defender: 'defensor',
  Midfielder: 'meio-campista',
  Forward: 'atacante',
};

const NIVEL_NA_PLANILHA: Readonly<Record<PriceTier, string>> = {
  Star: 'destaque',
  Regular: 'regular',
  Basic: 'basico',
};

const PLURAIS: Readonly<Record<AthletePosition, readonly [string, string]>> = {
  Goalkeeper: ['goleiro', 'goleiros'],
  Defender: ['defensor', 'defensores'],
  Midfielder: ['meio-campista', 'meio-campistas'],
  Forward: ['atacante', 'atacantes'],
};

/**
 * Passo 2: o elenco de cada time. A pessoa escolhe o time e escreve os atletas numa
 * grade, ou cola a lista do grupo; os novos entram pela importação em linhas e os que já
 * existem são alterados pelo cadastro de atletas. O nível de preço começa em Regular —
 * marcar os destaques é o que faz o orçamento obrigar escolhas.
 */
@Component({
  selector: 'app-setup-rosters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Dialog, RouterLink, SetupFooter],
  template: `
    <section class="etapa" aria-labelledby="passo-elencos">
      <h2 id="passo-elencos">2. Elencos</h2>
      <p class="intro">
        Escolha um time e escreva os atletas com a posição. A modalidade pede ao menos
        {{ store.minimo() }} atletas por time. O nível de preço começa em Regular: marque como
        Destaque quem joga mais, para o orçamento obrigar escolhas.
      </p>

      @if (faltasDePosicao().length > 0) {
        <app-alert tone="warning">
          Para alguém conseguir montar uma equipe no jogo, o campeonato ainda precisa de atletas:
          <ul class="faltas">
            @for (falta of faltasDePosicao(); track falta) {
              <li>{{ falta }}</li>
            }
          </ul>
        </app-alert>
      }

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @if (store.timesAtivos().length === 0) {
        <app-card>
          <p class="apoio">Nenhum time ainda. Os elencos vêm depois dos times.</p>
          <a class="acao" routerLink="../times">Cadastrar os times</a>
        </app-card>
      } @else {
        <div class="elencos">
          <ul class="elencos__times" aria-label="Times">
            @for (time of store.timesAtivos(); track time.id) {
              <li>
                <button
                  type="button"
                  class="elencos__time"
                  [class.elencos__time--atual]="time.id === timeAtual()?.id"
                  [attr.aria-pressed]="time.id === timeAtual()?.id"
                  (click)="escolher(time.id)"
                >
                  <span class="escudo" aria-hidden="true">{{ iniciais(time.name) }}</span>
                  <span class="elencos__nome">{{ time.name }}</span>
                  <span
                    class="elencos__contagem"
                    [class.elencos__contagem--ok]="contagem(time.id) >= store.minimo()"
                  >
                    {{ contagem(time.id) }} de {{ store.minimo() }}
                  </span>
                </button>
              </li>
            }
          </ul>

          @if (timeAtual(); as time) {
            <app-card [heading]="'Elenco de ' + time.name">
              <p class="dica">{{ resumoDePosicoes(time.id) }}</p>

              <div
                class="grade"
                style="--grade-colunas: minmax(0, 3fr) minmax(0, 2fr) minmax(0, 1.5fr) 6rem"
              >
                <div class="grade__cabecalho" aria-hidden="true">
                  <span>Atleta</span>
                  <span>Posição</span>
                  <span>Nível de preço</span>
                </div>

                <ul class="grade">
                  @for (atleta of elenco(); track atleta.id) {
                    <li class="grade__linha">
                      <div class="grade__celula">
                        <input
                          class="campo"
                          [attr.aria-label]="'Nome de ' + atleta.sportingName"
                          [attr.aria-invalid]="errosDeEdicao()[atleta.id] ? 'true' : null"
                          [maxLength]="nomeMax"
                          [value]="edicaoDe(atleta).nome"
                          (input)="editarExistente(atleta, 'nome', $event)"
                        />
                        @if (errosDeEdicao()[atleta.id]; as erro) {
                          <p class="erro">{{ erro }}</p>
                        }
                      </div>
                      <select
                        class="campo"
                        [attr.aria-label]="'Posição de ' + atleta.sportingName"
                        [disabled]="atleta.isMarketLocked"
                        (change)="editarExistente(atleta, 'posicao', $event)"
                      >
                        @for (opcao of posicoes; track opcao.value) {
                          <option
                            [value]="opcao.value"
                            [selected]="opcao.value === edicaoDe(atleta).posicao"
                          >
                            {{ opcao.label }}
                          </option>
                        }
                      </select>
                      <select
                        class="campo"
                        [attr.aria-label]="'Nível de preço de ' + atleta.sportingName"
                        [disabled]="atleta.isMarketLocked"
                        (change)="editarExistente(atleta, 'nivel', $event)"
                      >
                        @for (opcao of niveis; track opcao.value) {
                          <option
                            [value]="opcao.value"
                            [selected]="opcao.value === edicaoDe(atleta).nivel"
                          >
                            {{ opcao.label }}
                          </option>
                        }
                      </select>
                      <app-button
                        escrita
                        variant="destructive"
                        size="compact"
                        (pressed)="pedirDesligamento(atleta)"
                      >
                        Desligar<span class="sr-only"> {{ atleta.sportingName }}</span>
                      </app-button>
                    </li>
                  }

                  @for (linha of novasDoTime(); track linha.chave; let posicao = $index) {
                    <li class="grade__linha">
                      <div class="grade__celula">
                        <input
                          class="campo"
                          placeholder="Nome esportivo"
                          [attr.aria-label]="'Nome do atleta novo ' + (posicao + 1)"
                          [attr.aria-invalid]="erros()[linha.chave]?.nome ? 'true' : null"
                          [maxLength]="nomeMax"
                          [value]="linha.nome"
                          (input)="editar(linha.chave, 'nome', $event)"
                        />
                        @if (erros()[linha.chave]?.nome; as erro) {
                          <p class="erro">{{ erro }}</p>
                        }
                      </div>
                      <div class="grade__celula">
                        <select
                          class="campo"
                          [attr.aria-label]="'Posição do atleta novo ' + (posicao + 1)"
                          [attr.aria-invalid]="erros()[linha.chave]?.posicao ? 'true' : null"
                          (change)="editar(linha.chave, 'posicao', $event)"
                        >
                          <option value="" [selected]="linha.posicao === ''">Escolha</option>
                          @for (opcao of posicoes; track opcao.value) {
                            <option
                              [value]="opcao.value"
                              [selected]="opcao.value === linha.posicao"
                            >
                              {{ opcao.label }}
                            </option>
                          }
                        </select>
                        @if (erros()[linha.chave]?.posicao; as erro) {
                          <p class="erro">{{ erro }}</p>
                        }
                      </div>
                      <select
                        class="campo"
                        [attr.aria-label]="'Nível de preço do atleta novo ' + (posicao + 1)"
                        (change)="editar(linha.chave, 'nivel', $event)"
                      >
                        @for (opcao of niveis; track opcao.value) {
                          <option [value]="opcao.value" [selected]="opcao.value === linha.nivel">
                            {{ opcao.label }}
                          </option>
                        }
                      </select>
                      <app-button variant="ghost" size="compact" (pressed)="tirar(linha.chave)">
                        Tirar<span class="sr-only"> o atleta novo {{ posicao + 1 }}</span>
                      </app-button>
                    </li>
                  }
                </ul>
              </div>

              <div class="acoes">
                <app-button variant="secondary" (pressed)="maisLinhas(1)"
                  >Mais um atleta</app-button
                >
                <app-button
                  variant="ghost"
                  controls="colar-elenco"
                  [expanded]="colando()"
                  (pressed)="colando.set(!colando())"
                >
                  Colar uma lista
                </app-button>
              </div>

              @if (colando()) {
                <div id="colar-elenco" class="grade__celula">
                  <label for="lista-elenco">
                    Um atleta por linha, com a posição depois de um traço: Bia - meia. Numeração e
                    linhas em branco são ignoradas.
                  </label>
                  <textarea
                    id="lista-elenco"
                    class="campo campo--texto"
                    rows="8"
                    [value]="lista()"
                    (input)="lista.set(valor($event))"
                  ></textarea>
                  <app-button variant="secondary" (pressed)="aplicarLista()">
                    Pôr na grade
                  </app-button>
                </div>
              }
            </app-card>

            <div class="acoes">
              <app-button escrita [loading]="salvando()" (pressed)="salvar(time)">
                Salvar elenco de {{ time.name }}
              </app-button>
              @if (proximoTime(); as proximo) {
                <app-button variant="ghost" (pressed)="escolher(proximo.id)">
                  Próximo time: {{ proximo.name }}
                </app-button>
              }
            </div>
          }
        </div>
      }

      <app-setup-footer passo="elencos" />
    </section>

    <app-dialog
      [heading]="'Desligar ' + (desligando()?.sportingName ?? '') + '?'"
      [open]="desligando() !== null"
      (dismissed)="desligando.set(null)"
    >
      <p>O atleta fica indisponível para novas compras. Time, preço e histórico ficam guardados.</p>
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="salvando()" (pressed)="desligando.set(null)">
          Cancelar
        </app-button>
        <app-button escrita variant="danger" [loading]="salvando()" (pressed)="desligar()">
          Desligar atleta
        </app-button>
      </div>
    </app-dialog>
  `,
  styleUrls: [
    '../../organizer.scss',
    '../competition-pages.scss',
    './setup.scss',
    './setup-rosters.scss',
  ],
})
export class SetupRostersStep {
  protected readonly store = inject(SetupStore);
  private readonly imports = inject(ImportService);
  private readonly athletes = inject(AthleteService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly nomeMax = ATHLETE_NAME_MAX;
  protected readonly posicoes = POSICOES;
  protected readonly niveis = NIVEIS;
  private proximaChave = 0;

  private readonly escolhido = signal<string | null>(null);
  /** Linhas novas por time: trocar de time não perde o que foi digitado. */
  private readonly novas = signal<Readonly<Record<string, readonly LinhaAtleta[]>>>({});
  private readonly edicoes = signal<Readonly<Record<string, Edicao>>>({});
  protected readonly erros = signal<Readonly<Record<number, ErroDaLinha>>>({});
  protected readonly errosDeEdicao = signal<Readonly<Record<string, string>>>({});
  protected readonly colando = signal(false);
  protected readonly lista = signal('');
  protected readonly salvando = signal(false);
  protected readonly desligando = signal<Athlete | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  /** O time escolhido; antes da escolha, o primeiro que ainda está incompleto. */
  protected readonly timeAtual = computed<RealTeam | null>(() => {
    const times = this.store.timesAtivos();
    return (
      times.find((time) => time.id === this.escolhido()) ??
      this.store.timesIncompletos()[0] ??
      times[0] ??
      null
    );
  });

  protected readonly elenco = computed(() => {
    const time = this.timeAtual();
    return time ? this.store.elencoDe(time.id) : [];
  });

  protected readonly novasDoTime = computed(() => {
    const time = this.timeAtual();
    return time ? (this.novas()[time.id] ?? []) : [];
  });

  /** O próximo time da lista, de volta ao primeiro depois do último. */
  protected readonly proximoTime = computed(() => {
    const times = this.store.timesAtivos();
    const atual = times.findIndex((time) => time.id === this.timeAtual()?.id);
    return times.length > 1 ? times[(atual + 1) % times.length] : null;
  });

  protected readonly faltasDePosicao = computed(() =>
    (this.store.dados()?.readiness.items ?? [])
      .filter((item) => item.code === 'not_enough_athletes')
      .map((item) => item.message.replace(/^Faltam atletas na posição /, '')),
  );

  constructor() {
    // Um time sem ninguém já abre com linhas para digitar.
    const time = this.timeAtual();
    if (time && this.store.elencoDe(time.id).length === 0) this.maisLinhas(5);
  }

  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected contagem(teamId: string): number {
    return this.store.elencoDe(teamId).length;
  }

  protected resumoDePosicoes(teamId: string): string {
    const elenco = this.store.elencoDe(teamId);
    if (elenco.length === 0) return 'Ninguém inscrito ainda.';
    return POSICOES.map(({ value }) => {
      const total = elenco.filter((atleta) => atleta.position === value).length;
      return `${total} ${PLURAIS[value][total === 1 ? 0 : 1]}`;
    }).join(' · ');
  }

  protected valor(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  protected escolher(teamId: string): void {
    this.escolhido.set(teamId);
    this.erros.set({});
    this.errosDeEdicao.set({});
    this.colando.set(false);
    if (this.store.elencoDe(teamId).length === 0 && !this.novas()[teamId]?.length) {
      this.maisLinhas(5);
    }
  }

  protected edicaoDe(atleta: Athlete): Edicao {
    return (
      this.edicoes()[atleta.id] ?? {
        nome: atleta.sportingName,
        posicao: atleta.position,
        nivel: atleta.priceTier,
      }
    );
  }

  protected editarExistente(atleta: Athlete, campo: keyof Edicao, event: Event): void {
    const texto = this.valor(event);
    this.edicoes.update((atual) => ({
      ...atual,
      [atleta.id]: { ...this.edicaoDe(atleta), [campo]: texto },
    }));
  }

  protected maisLinhas(quantidade: number): void {
    const time = this.timeAtual();
    if (!time) return;
    this.acrescentar(
      time.id,
      Array.from({ length: quantidade }, () => ({
        chave: this.proximaChave++,
        nome: '',
        posicao: '' as const,
        nivel: 'Regular' as const,
      })),
    );
  }

  protected editar(chave: number, campo: 'nome' | 'posicao' | 'nivel', event: Event): void {
    const time = this.timeAtual();
    if (!time) return;
    const texto = this.valor(event);
    this.novas.update((atual) => ({
      ...atual,
      [time.id]: (atual[time.id] ?? []).map((linha) =>
        linha.chave === chave ? { ...linha, [campo]: texto } : linha,
      ),
    }));
  }

  protected tirar(chave: number): void {
    const time = this.timeAtual();
    if (!time) return;
    this.novas.update((atual) => ({
      ...atual,
      [time.id]: (atual[time.id] ?? []).filter((linha) => linha.chave !== chave),
    }));
  }

  /** A lista colada ocupa o lugar das linhas em branco e vai para o fim da grade. */
  protected aplicarLista(): void {
    const time = this.timeAtual();
    if (!time) return;
    const lidos = parseRosterList(this.lista()).map((atleta) => ({
      chave: this.proximaChave++,
      nome: atleta.name,
      posicao: atleta.position ?? ('' as const),
      nivel: 'Regular' as const,
    }));
    this.novas.update((atual) => ({
      ...atual,
      [time.id]: (atual[time.id] ?? []).filter((linha) => linha.nome.trim()),
    }));
    this.acrescentar(time.id, lidos);
    this.lista.set('');
    this.colando.set(false);
    const semPosicao = lidos.filter((linha) => linha.posicao === '').length;
    if (semPosicao > 0) {
      this.avisar(
        'warning',
        `${lidos.length} na grade. Escolha a posição de ${semPosicao} ${semPosicao === 1 ? 'atleta' : 'atletas'}.`,
      );
    }
  }

  protected salvar(time: RealTeam): void {
    this.retorno.set(null);
    const novos = (this.novas()[time.id] ?? []).filter((linha) => linha.nome.trim());
    const alterados = this.elenco().filter((atleta) => {
      const edicao = this.edicoes()[atleta.id];
      return (
        edicao !== undefined &&
        (edicao.nome.trim() !== atleta.sportingName ||
          edicao.posicao !== atleta.position ||
          edicao.nivel !== atleta.priceTier)
      );
    });

    if (!this.validar(novos, alterados)) return;
    if (novos.length === 0 && alterados.length === 0) {
      this.avisar('warning', 'Nada mudou neste elenco.');
      return;
    }

    const id = this.store.campeonatoId();
    const linhas: ImportRow[] = novos.map((linha) => ({
      nome_esportivo: linha.nome.trim(),
      time: time.name,
      posicao: POSICAO_NA_PLANILHA[linha.posicao as AthletePosition],
      nivel_preco: NIVEL_NA_PLANILHA[linha.nivel],
    }));

    this.salvando.set(true);
    of(null)
      .pipe(
        concatMap(() =>
          linhas.length
            ? marcar('novos', this.imports.commitRows(id, 'atletas', linhas))
            : of(null),
        ),
        concatMap(() =>
          alterados.length
            ? marcar(
                'alterados',
                forkJoin(
                  alterados.map((atleta) => {
                    const edicao = this.edicoes()[atleta.id];
                    return this.athletes.update(
                      id,
                      atleta.id,
                      {
                        sportingName: edicao.nome.trim(),
                        position: edicao.posicao,
                        realTeamId: atleta.realTeamId,
                        priceTier: edicao.nivel,
                        initialPriceOverride: atleta.initialPriceOverride,
                      },
                      atleta.version,
                    );
                  }),
                ),
              )
            : of(null),
        ),
      )
      .subscribe({
        complete: () => {
          this.salvando.set(false);
          this.novas.update((atual) => ({ ...atual, [time.id]: [] }));
          this.edicoes.set({});
          this.avisar('success', `Elenco de ${time.name} salvo.`);
          this.store.reload();
        },
        error: (erro: { readonly etapa: 'novos' | 'alterados'; readonly falha: ApiFailure }) => {
          this.salvando.set(false);
          if (erro.etapa === 'alterados') {
            // Os novos, se havia, já entraram: sair da grade evita mandá-los de novo.
            this.novas.update((atual) => ({ ...atual, [time.id]: [] }));
            this.store.reload();
            this.avisar(
              'warning',
              erro.falha.status === 409
                ? 'Alguém alterou um atleta enquanto você editava. A lista foi atualizada.'
                : `Os atletas novos foram salvos, mas uma alteração não: ${erro.falha.message}`,
            );
            this.edicoes.set({});
            return;
          }

          const erros: Record<number, ErroDaLinha> = {};
          for (const problema of importIssues(erro.falha)) {
            const indice = rowIndexOf(problema);
            const linha = indice === null ? undefined : novos[indice];
            if (!linha) continue;
            const campo = problema.column === 'posicao' ? 'posicao' : 'nome';
            erros[linha.chave] = { ...erros[linha.chave], [campo]: problema.message };
          }
          this.erros.set(erros);
          this.avisar(
            'danger',
            Object.keys(erros).length > 0
              ? 'Nada foi salvo. Corrija as linhas marcadas.'
              : erro.falha.message,
          );
        },
      });
  }

  protected pedirDesligamento(atleta: Athlete): void {
    this.desligando.set(atleta);
  }

  protected desligar(): void {
    const atleta = this.desligando();
    if (!atleta || this.salvando()) return;
    this.salvando.set(true);
    this.athletes.release(this.store.campeonatoId(), atleta.id).subscribe({
      next: () => {
        this.salvando.set(false);
        this.desligando.set(null);
        this.avisar('success', `${atleta.sportingName} desligado.`);
        this.store.reload();
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        this.desligando.set(null);
        this.avisar('danger', falha.message);
      },
    });
  }

  private acrescentar(teamId: string, linhas: readonly LinhaAtleta[]): void {
    this.novas.update((atual) => ({ ...atual, [teamId]: [...(atual[teamId] ?? []), ...linhas] }));
  }

  private validar(novos: readonly LinhaAtleta[], alterados: readonly Athlete[]): boolean {
    const erros: Record<number, ErroDaLinha> = {};
    const errosDeEdicao: Record<string, string> = {};
    // O nome esportivo é único no campeonato inteiro, não só no time.
    const usados = new Map(
      this.store
        .atletasAtivos()
        .map((atleta) => [
          chave(this.edicoes()[atleta.id]?.nome ?? atleta.sportingName),
          atleta.id,
        ]),
    );

    for (const atleta of alterados) {
      const nome = this.edicoes()[atleta.id].nome.trim();
      if (nome.length < ATHLETE_NAME_MIN || nome.length > ATHLETE_NAME_MAX) {
        errosDeEdicao[atleta.id] = `Use de ${ATHLETE_NAME_MIN} a ${ATHLETE_NAME_MAX} caracteres.`;
      } else if ((usados.get(chave(nome)) ?? atleta.id) !== atleta.id) {
        errosDeEdicao[atleta.id] = 'Outro atleta do campeonato já usa esse nome.';
      }
    }

    for (const linha of novos) {
      const nome = linha.nome.trim();
      const erro: { nome?: string; posicao?: string } = {};
      if (nome.length < ATHLETE_NAME_MIN || nome.length > ATHLETE_NAME_MAX) {
        erro.nome = `Use de ${ATHLETE_NAME_MIN} a ${ATHLETE_NAME_MAX} caracteres.`;
      } else if (usados.has(chave(nome))) {
        erro.nome = 'Já existe um atleta com esse nome no campeonato.';
      }
      if (linha.posicao === '') erro.posicao = 'Escolha a posição.';
      usados.set(chave(nome), `novo-${linha.chave}`);
      if (erro.nome || erro.posicao) erros[linha.chave] = erro;
    }

    this.erros.set(erros);
    this.errosDeEdicao.set(errosDeEdicao);
    const valido = Object.keys(erros).length === 0 && Object.keys(errosDeEdicao).length === 0;
    if (!valido) this.avisar('danger', 'Corrija os campos marcados antes de salvar.');
    return valido;
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}

/** Marca a falha com a etapa em que aconteceu, para o erro voltar à linha certa. */
function marcar<T>(etapa: 'novos' | 'alterados', fluxo: Observable<T>): Observable<T> {
  return fluxo.pipe(catchError((falha: ApiFailure) => throwError(() => ({ etapa, falha }))));
}

function chave(nome: string): string {
  return nome.trim().toLocaleLowerCase('pt-BR');
}
