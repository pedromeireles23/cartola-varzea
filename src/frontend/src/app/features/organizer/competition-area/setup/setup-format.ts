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
import { Observable, concat, concatMap, from, last, of, toArray } from 'rxjs';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Alert, Button, Card, Dialog } from '../../../../shared/ui';
import {
  DEFAULT_TIEBREAKERS,
  FORMAT_LABELS,
  Stage,
  StageInput,
  StageService,
} from '../stage.service';
import { SetupFooter } from './competition-setup';
import { bracketSize, knockoutStageName, knockoutStageNames, shuffle } from './fixture-generator';
import { SetupStore } from './setup-store';
import { teamInitials } from './setup-text';

type Modelo = 'pontos' | 'grupos' | 'matamata';

const MODELOS: readonly {
  readonly valor: Modelo;
  readonly titulo: string;
  readonly texto: string;
}[] = [
  {
    valor: 'pontos',
    titulo: 'Pontos corridos',
    texto: 'Todos contra todos. Quem somar mais pontos é campeão.',
  },
  {
    valor: 'grupos',
    titulo: 'Grupos e mata-mata',
    texto: 'Todos contra todos dentro de cada grupo; os melhores de cada um vão para o mata-mata.',
  },
  {
    valor: 'matamata',
    titulo: 'Mata-mata',
    texto: 'Eliminação direta desde o primeiro jogo, até a final.',
  },
];

const LETRAS = 'ABCDEFGHIJKLMNOP';

/**
 * Passo 3: o formato do campeonato, escolhido entre modelos prontos. O modelo vira as
 * fases que a tela de Fases mostra — pontos corridos é uma fase de grupo único; grupos e
 * mata-mata, uma fase de grupos e as fases da chave —, e os times entram na primeira
 * fase. Quem precisar de outro arranjo monta à mão em Fases.
 */
@Component({
  selector: 'app-setup-format',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Dialog, RouterLink, SetupFooter],
  template: `
    <section class="etapa" aria-labelledby="passo-formato">
      <h2 id="passo-formato">3. Formato</h2>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @if (fases().length > 0) {
        <p class="intro">
          O formato está definido. A tela Fases permite trocar nomes, grupos e critérios de
          desempate.
        </p>
        <app-card heading="Fases do campeonato">
          <ol class="fases">
            @for (fase of fases(); track fase.id) {
              <li class="fases__item">
                <span class="fases__nome">{{ fase.name }}</span>
                <span class="dica"> {{ formato(fase) }} · {{ participantes(fase) }} </span>
              </li>
            }
          </ol>
          <div class="acoes">
            <a class="acao" routerLink="../../fases">Ajustar em Fases</a>
            @if (podeRecomecar()) {
              <app-button escrita variant="destructive" (pressed)="confirmando.set(true)">
                Recomeçar o formato
              </app-button>
            }
          </div>
          @if (!podeRecomecar()) {
            <p class="dica">
              Já há jogos marcados, então o formato não recomeça daqui. Para mudar, remova os jogos
              em Rodadas antes.
            </p>
          }
        </app-card>
      } @else if (store.timesAtivos().length < 2) {
        <p class="intro">O formato depende dos times que vão jogar.</p>
        <app-card>
          <p class="apoio">Cadastre ao menos dois times antes de escolher o formato.</p>
          <a class="acao" routerLink="../times">Cadastrar os times</a>
        </app-card>
      } @else {
        <p class="intro">
          Escolha como os {{ store.timesAtivos().length }} times vão se enfrentar. Turno e returno,
          ida e volta, datas e horários ficam para o próximo passo.
        </p>

        <fieldset class="modelos">
          <legend class="sr-only">Formato do campeonato</legend>
          @for (opcao of modelos; track opcao.valor) {
            <label class="modelo" [class.modelo--escolhido]="modelo() === opcao.valor">
              <input
                type="radio"
                name="modelo"
                [value]="opcao.valor"
                [checked]="modelo() === opcao.valor"
                (change)="escolher(opcao.valor)"
              />
              <span class="modelo__titulo">{{ opcao.titulo }}</span>
              <span class="modelo__texto">{{ opcao.texto }}</span>
            </label>
          }
        </fieldset>

        @if (modelo() === 'pontos') {
          <app-card>
            <p class="apoio">
              Os {{ store.timesAtivos().length }} times entram numa fase só, “Pontos corridos”, num
              grupo único. Desempate: vitórias, saldo, gols marcados, confronto direto e cartões.
            </p>
          </app-card>
        }

        @if (modelo() === 'matamata') {
          <app-card>
            <p class="apoio">{{ textoMataMata() }}</p>
          </app-card>
        }

        @if (modelo() === 'grupos') {
          <app-card heading="Grupos">
            <div class="opcoes">
              <label class="grade__celula">
                Quantidade de grupos
                <select class="campo" (change)="trocarGrupos($event)">
                  @for (n of opcoesDeGrupos(); track n) {
                    <option [value]="n" [selected]="n === grupos()">{{ n }} grupos</option>
                  }
                </select>
              </label>
              <label class="grade__celula">
                Classificados por grupo
                <select class="campo" (change)="trocarClassificados($event)">
                  @for (n of opcoesDeClassificados(); track n) {
                    <option [value]="n" [selected]="n === classificadosValidos()">{{ n }}</option>
                  }
                </select>
              </label>
            </div>
            <p class="dica">{{ textoDaChave() }}</p>

            <div class="acoes">
              <app-button variant="secondary" (pressed)="sortear()">Sortear os grupos</app-button>
              <app-button variant="ghost" (pressed)="emOrdem()">Distribuir em ordem</app-button>
            </div>

            <ul class="grade" style="--grade-colunas: minmax(0, 1fr) 9rem">
              @for (time of store.timesAtivos(); track time.id) {
                <li class="grade__linha">
                  <div class="grade__fixo">
                    <span class="escudo" aria-hidden="true">{{ iniciais(time.name) }}</span>
                    {{ time.name }}
                  </div>
                  <select
                    class="campo"
                    [attr.aria-label]="'Grupo de ' + time.name"
                    (change)="trocarGrupoDe(time.id, $event)"
                  >
                    @for (letra of letras(); track letra) {
                      <option [value]="letra" [selected]="grupoDe(time.id) === letra">
                        Grupo {{ letra }}
                      </option>
                    }
                  </select>
                </li>
              }
            </ul>
            @if (grupoVazio()) {
              <p class="erro">Cada grupo precisa de ao menos dois times.</p>
            }
          </app-card>
        }

        <div class="acoes">
          <app-button
            escrita
            [disabled]="modelo() === null || (modelo() === 'grupos' && grupoVazio())"
            [loading]="salvando()"
            (pressed)="criar()"
          >
            Usar este formato
          </app-button>
        </div>
      }

      <app-setup-footer passo="formato" />
    </section>

    <app-dialog
      heading="Recomeçar o formato?"
      [open]="confirmando()"
      (dismissed)="confirmando.set(false)"
    >
      <p>As fases e a distribuição dos times nelas são apagadas. Times e elencos continuam.</p>
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="salvando()" (pressed)="confirmando.set(false)">
          Cancelar
        </app-button>
        <app-button escrita variant="danger" [loading]="salvando()" (pressed)="recomecar()">
          Apagar as fases
        </app-button>
      </div>
    </app-dialog>
  `,
  styleUrls: [
    '../../organizer.scss',
    '../competition-pages.scss',
    './setup.scss',
    './setup-format.scss',
  ],
})
export class SetupFormatStep {
  protected readonly store = inject(SetupStore);
  private readonly stages = inject(StageService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly modelos = MODELOS;
  protected readonly modelo = signal<Modelo | null>(null);
  protected readonly grupos = signal(2);
  protected readonly classificados = signal(2);
  /** Grupo de cada time, pela letra. */
  private readonly distribuicao = signal<Readonly<Record<string, string>>>({});
  protected readonly salvando = signal(false);
  protected readonly confirmando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly fases = computed(() => this.store.dados()?.stages ?? []);
  protected readonly podeRecomecar = computed(() => this.store.jogos().length === 0);

  protected readonly letras = computed(() => [...LETRAS.slice(0, this.grupos())]);

  /** Grupos possíveis: ao menos dois times em cada. */
  protected readonly opcoesDeGrupos = computed(() => {
    const maximo = Math.min(8, Math.floor(this.store.timesAtivos().length / 2));
    return Array.from({ length: Math.max(0, maximo - 1) }, (_, indice) => indice + 2);
  });

  protected readonly opcoesDeClassificados = computed(() => {
    const menor = Math.min(...this.letras().map((letra) => this.timesNoGrupo(letra)));
    return [1, 2, 4].filter((n) => n < Math.max(2, menor) || n === 1);
  });

  /** O escolhido, ou o maior possível quando a troca de grupos o deixou grande demais. */
  protected readonly classificadosValidos = computed(() => {
    const opcoes = this.opcoesDeClassificados();
    return opcoes.includes(this.classificados()) ? this.classificados() : Math.max(...opcoes);
  });

  protected readonly grupoVazio = computed(() =>
    this.letras().some((letra) => this.timesNoGrupo(letra) < 2),
  );

  protected readonly textoDaChave = computed(() => {
    const total = this.grupos() * this.classificadosValidos();
    if (total < 2) return 'Com um classificado só, o campeão sai da fase de grupos.';
    return `Vão ${total} times para o mata-mata: ${knockoutStageNames(total).join(', ')}.`;
  });

  protected readonly textoMataMata = computed(() => {
    const times = this.store.timesAtivos().length;
    const folgas = bracketSize(times) - times;
    const fases = knockoutStageNames(times).join(', ');
    return folgas === 0
      ? `Os ${times} times entram na chave: ${fases}. A ordem dos confrontos fica para a Tabela.`
      : `Os ${times} times entram na chave: ${fases}. Como ${times} não fecha a chave, ${folgas} ` +
          `times passam direto pela ${knockoutStageName(times).toLocaleLowerCase('pt-BR')}. ` +
          'Quem folga é decidido na Tabela.';
  });

  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected formato(fase: Stage): string {
    if (fase.format === 'Knockout') return FORMAT_LABELS.Knockout;
    return fase.groups.length === 1 ? 'Grupo único' : `${fase.groups.length} grupos`;
  }

  protected participantes(fase: Stage): string {
    const total = fase.participants.length;
    if (total === 0) return 'times a definir';
    return total === 1 ? '1 time' : `${total} times`;
  }

  protected escolher(modelo: Modelo): void {
    this.modelo.set(modelo);
    if (modelo === 'grupos') {
      const opcoes = this.opcoesDeGrupos();
      if (!opcoes.includes(this.grupos())) this.grupos.set(opcoes[0] ?? 2);
      this.emOrdem();
    }
  }

  protected trocarGrupos(event: Event): void {
    this.grupos.set(Number((event.target as HTMLSelectElement).value));
    this.emOrdem();
  }

  protected trocarClassificados(event: Event): void {
    this.classificados.set(Number((event.target as HTMLSelectElement).value));
  }

  protected grupoDe(teamId: string): string {
    return this.distribuicao()[teamId] ?? 'A';
  }

  protected trocarGrupoDe(teamId: string, event: Event): void {
    const letra = (event.target as HTMLSelectElement).value;
    this.distribuicao.update((atual) => ({ ...atual, [teamId]: letra }));
  }

  /** Em ordem de cadastro, um para cada grupo: A, B, C, A, B, C… */
  protected emOrdem(): void {
    this.distribuir(this.store.timesAtivos().map((time) => time.id));
  }

  protected sortear(): void {
    this.distribuir(shuffle(this.store.timesAtivos().map((time) => time.id)));
    this.avisar('success', 'Grupos sorteados. Ajuste à mão o que quiser antes de usar.');
  }

  protected criar(): void {
    const modelo = this.modelo();
    if (!modelo || this.salvando()) return;

    const id = this.store.campeonatoId();
    const times = this.store.timesAtivos();
    this.salvando.set(true);

    let fluxo: Observable<unknown>;
    if (modelo === 'pontos') {
      fluxo = this.criarFase(id, grupos('Pontos corridos', ['Grupo único'])).pipe(
        concatMap((fase) =>
          this.stages.setParticipants(
            id,
            fase.id,
            times.map((time) => ({ realTeamId: time.id, stageGroupId: fase.groups[0].id })),
            fase.version,
          ),
        ),
      );
    } else if (modelo === 'grupos') {
      const nomes = this.letras().map((letra) => `Grupo ${letra}`);
      const chave = this.grupos() * this.classificadosValidos();
      fluxo = concat(
        this.criarFase(id, grupos('Fase de grupos', nomes)).pipe(
          concatMap((fase) =>
            this.stages.setParticipants(
              id,
              fase.id,
              times.map((time) => ({
                realTeamId: time.id,
                stageGroupId:
                  fase.groups.find((grupo) => grupo.name === `Grupo ${this.grupoDe(time.id)}`)
                    ?.id ?? null,
              })),
              fase.version,
            ),
          ),
        ),
        this.criarMataMata(id, chave >= 2 ? knockoutStageNames(chave) : []),
      );
    } else {
      fluxo = this.criarMataMata(id, knockoutStageNames(times.length)).pipe(
        concatMap((fases) =>
          this.stages.setParticipants(
            id,
            fases[0].id,
            times.map((time) => ({ realTeamId: time.id, stageGroupId: null })),
            fases[0].version,
          ),
        ),
      );
    }

    fluxo.pipe(last(undefined, null)).subscribe({
      next: () => {
        this.salvando.set(false);
        this.modelo.set(null);
        this.avisar('success', 'Formato definido. Agora gere a tabela de jogos.');
        this.store.reload();
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        // Uma parte pode ter sido criada: recarregar mostra o que existe e permite recomeçar.
        this.store.reload();
        this.avisar('danger', `O formato não foi criado por inteiro. ${falha.message}`);
      },
    });
  }

  protected recomecar(): void {
    if (this.salvando()) return;
    const id = this.store.campeonatoId();
    this.salvando.set(true);
    // De trás para a frente: a renumeração das fases que ficam não atrapalha a remoção.
    from([...this.fases()].reverse())
      .pipe(
        concatMap((fase) => this.stages.remove(id, fase.id)),
        toArray(),
      )
      .subscribe({
        next: () => {
          this.salvando.set(false);
          this.confirmando.set(false);
          this.avisar('success', 'Fases apagadas. Escolha o formato de novo.');
          this.store.reload();
        },
        error: (falha: ApiFailure) => {
          this.salvando.set(false);
          this.confirmando.set(false);
          this.store.reload();
          this.avisar('danger', falha.message);
        },
      });
  }

  private criarFase(id: string, fase: StageInput): Observable<Stage> {
    return this.stages.create(id, fase);
  }

  /** As fases da chave, em ordem, sem times: quem joga cada uma é confirmado depois. */
  private criarMataMata(id: string, nomes: readonly string[]): Observable<Stage[]> {
    if (nomes.length === 0) return of([]);
    return from(nomes).pipe(
      concatMap((nome) =>
        this.criarFase(id, { name: nome, format: 'Knockout', groups: [], tiebreakers: [] }),
      ),
      toArray(),
    );
  }

  private distribuir(ids: readonly string[]): void {
    const letras = this.letras();
    this.distribuicao.set(
      Object.fromEntries(ids.map((id, indice) => [id, letras[indice % letras.length]])),
    );
  }

  private timesNoGrupo(letra: string): number {
    return this.store.timesAtivos().filter((time) => this.grupoDe(time.id) === letra).length;
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}

function grupos(nome: string, nomes: readonly string[]): StageInput {
  return {
    name: nome,
    format: 'Groups',
    groups: nomes.map((grupo) => ({ id: null, name: grupo })),
    tiebreakers: DEFAULT_TIEBREAKERS,
  };
}
