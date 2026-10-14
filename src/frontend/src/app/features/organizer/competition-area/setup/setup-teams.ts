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
import { Observable, catchError, concatMap, forkJoin, of, throwError } from 'rxjs';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Alert, Button, Card } from '../../../../shared/ui';
import { COACH_NAME_MAX, COACH_NAME_MIN, CoachService } from '../coach.service';
import { ImportRow, ImportService, importIssues, rowIndexOf } from '../import.service';
import { REAL_TEAM_NAME_MAX, REAL_TEAM_NAME_MIN, RealTeam } from '../real-team.service';
import { SetupFooter } from './competition-setup';
import { teamInitials } from './setup-text';
import { SetupStore } from './setup-store';

interface NovoTime {
  readonly chave: number;
  readonly nome: string;
  readonly tecnico: string;
}

interface ErroDaLinha {
  readonly nome?: string;
  readonly tecnico?: string;
}

/** A gravação falhou em uma das etapas; a etapa diz a que linhas o erro se refere. */
interface FalhaNaEtapa {
  readonly etapa: 'times' | 'tecnicos' | 'edicoes';
  readonly falha: ApiFailure;
}

/**
 * Passo 1: os times e o nome do técnico de cada um, numa grade só. Os times novos vão
 * pela importação em linhas — as mesmas regras da planilha, sem planilha —, e o técnico
 * de um time que já existe é alterado pelo cadastro de técnicos, mantendo o preço.
 */
@Component({
  selector: 'app-setup-teams',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, SetupFooter],
  template: `
    <section class="etapa" aria-labelledby="passo-times">
      <h2 id="passo-times">1. Times e técnicos</h2>
      <p class="intro">
        Escreva os times que vão jogar. O técnico é opcional: sem nome, o jogo mostra “Técnico do” e
        o nome do time. Para renomear ou arquivar um time, use a tela Times.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      <app-card [heading]="'Times do campeonato (' + store.timesAtivos().length + ')'">
        <div class="grade" style="--grade-colunas: minmax(0, 3fr) minmax(0, 2fr) 5.5rem">
          <div class="grade__cabecalho" aria-hidden="true">
            <span>Time</span>
            <span>Técnico (opcional)</span>
          </div>

          <ul class="grade">
            @for (time of store.timesAtivos(); track time.id) {
              <li class="grade__linha">
                <div class="grade__fixo">
                  <span class="escudo" aria-hidden="true">{{ iniciais(time.name) }}</span>
                  {{ time.name }}
                </div>
                <div class="grade__celula">
                  <input
                    class="campo"
                    [attr.aria-label]="'Técnico de ' + time.name"
                    [attr.aria-invalid]="erroDoTecnico(time.id) ? 'true' : null"
                    [maxLength]="nomeMax"
                    [value]="tecnicoDe(time)"
                    (input)="editarTecnico(time, $event)"
                  />
                  @if (erroDoTecnico(time.id); as erro) {
                    <p class="erro">{{ erro }}</p>
                  }
                </div>
              </li>
            }

            @for (linha of novas(); track linha.chave; let posicao = $index) {
              <li class="grade__linha">
                <div class="grade__celula">
                  <input
                    class="campo"
                    placeholder="Nome do time"
                    [attr.aria-label]="'Nome do time novo ' + (posicao + 1)"
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
                  <input
                    class="campo"
                    placeholder="Técnico"
                    [attr.aria-label]="'Técnico do time novo ' + (posicao + 1)"
                    [attr.aria-invalid]="erros()[linha.chave]?.tecnico ? 'true' : null"
                    [maxLength]="nomeMax"
                    [value]="linha.tecnico"
                    (input)="editar(linha.chave, 'tecnico', $event)"
                  />
                  @if (erros()[linha.chave]?.tecnico; as erro) {
                    <p class="erro">{{ erro }}</p>
                  }
                </div>
                <app-button variant="ghost" size="compact" (pressed)="tirar(linha.chave)">
                  Tirar<span class="sr-only"> o time novo {{ posicao + 1 }}</span>
                </app-button>
              </li>
            }
          </ul>
        </div>

        <div class="acoes">
          <app-button variant="secondary" (pressed)="maisLinhas(1)">Mais um time</app-button>
          <app-button
            variant="ghost"
            controls="colar-times"
            [expanded]="colando()"
            (pressed)="colando.set(!colando())"
          >
            Colar uma lista
          </app-button>
        </div>

        @if (colando()) {
          <div id="colar-times" class="grade__celula">
            <label for="lista-times">
              Um time por linha. Para incluir o técnico, use um traço: União da Vila - Seu Zé
            </label>
            <textarea
              id="lista-times"
              class="campo campo--texto"
              rows="6"
              [value]="lista()"
              (input)="lista.set(valor($event))"
            ></textarea>
            <app-button variant="secondary" (pressed)="aplicarLista()">Pôr na grade</app-button>
          </div>
        }
      </app-card>

      <div class="acoes">
        <app-button escrita [loading]="salvando()" (pressed)="salvar()">Salvar times</app-button>
      </div>

      <app-setup-footer passo="times" />
    </section>
  `,
  styleUrls: ['../../organizer.scss', '../competition-pages.scss', './setup.scss'],
})
export class SetupTeamsStep {
  protected readonly store = inject(SetupStore);
  private readonly imports = inject(ImportService);
  private readonly coaches = inject(CoachService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly nomeMax = REAL_TEAM_NAME_MAX;
  private proximaChave = 0;

  protected readonly novas = signal<readonly NovoTime[]>([]);
  /** Nome do técnico alterado na grade, por time. */
  protected readonly tecnicos = signal<Readonly<Record<string, string>>>({});
  protected readonly erros = signal<Readonly<Record<number, ErroDaLinha>>>({});
  protected readonly errosDeTecnico = signal<Readonly<Record<string, string>>>({});
  protected readonly colando = signal(false);
  protected readonly lista = signal('');
  protected readonly salvando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  /** Os times que já existem, com o técnico, para comparar o que mudou. */
  private readonly existentes = computed(() =>
    this.store.timesAtivos().map((time) => ({ time, tecnico: this.store.tecnicoDe(time.id) })),
  );

  constructor() {
    // Campeonato sem time começa com linhas prontas para digitar.
    if (this.store.timesAtivos().length === 0) this.maisLinhas(4);
  }

  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected tecnicoDe(time: RealTeam): string {
    return this.tecnicos()[time.id] ?? this.store.tecnicoDe(time.id)?.displayName ?? '';
  }

  protected erroDoTecnico(teamId: string): string | undefined {
    return this.errosDeTecnico()[teamId];
  }

  protected valor(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected maisLinhas(quantidade: number): void {
    const novas = Array.from({ length: quantidade }, () => ({
      chave: this.proximaChave++,
      nome: '',
      tecnico: '',
    }));
    this.novas.update((linhas) => [...linhas, ...novas]);
  }

  protected editar(chave: number, campo: 'nome' | 'tecnico', event: Event): void {
    const texto = this.valor(event);
    this.novas.update((linhas) =>
      linhas.map((linha) => (linha.chave === chave ? { ...linha, [campo]: texto } : linha)),
    );
  }

  protected editarTecnico(time: RealTeam, event: Event): void {
    const texto = this.valor(event);
    this.tecnicos.update((atual) => ({ ...atual, [time.id]: texto }));
  }

  protected tirar(chave: number): void {
    this.novas.update((linhas) => linhas.filter((linha) => linha.chave !== chave));
  }

  /** A lista colada substitui as linhas em branco e entra no fim da grade. */
  protected aplicarLista(): void {
    const linhas = this.lista()
      .split(/\r?\n/)
      .map((linha) => linha.trim())
      .filter((linha) => linha.length > 0)
      .map((linha) => {
        const [nome, ...tecnico] = linha.split(/\s[-–—]\s|;|\t/);
        return { chave: this.proximaChave++, nome: nome.trim(), tecnico: tecnico.join(' ').trim() };
      });
    this.novas.update((atuais) => [
      ...atuais.filter((linha) => linha.nome.trim() || linha.tecnico.trim()),
      ...linhas,
    ]);
    this.lista.set('');
    this.colando.set(false);
  }

  protected salvar(): void {
    this.retorno.set(null);
    const novos = this.novas().filter((linha) => linha.nome.trim() || linha.tecnico.trim());
    const edicoes = this.existentes().filter(
      ({ time, tecnico }) =>
        time.id in this.tecnicos() &&
        this.tecnicos()[time.id].trim() !== (tecnico?.displayName ?? ''),
    );

    if (
      !this.validar(
        novos,
        edicoes.map(({ time }) => time),
      )
    )
      return;
    if (novos.length === 0 && edicoes.length === 0) {
      this.avisar('warning', 'Nada mudou: escreva um time novo ou altere um técnico.');
      return;
    }

    const id = this.store.campeonatoId();
    const times: ImportRow[] = novos.map((linha) => ({ nome: linha.nome.trim() }));
    const comTecnico = novos.filter((linha) => linha.tecnico.trim());
    const tecnicos: ImportRow[] = comTecnico.map((linha) => ({
      time: linha.nome.trim(),
      nome: linha.tecnico.trim(),
    }));

    this.salvando.set(true);
    of(null)
      .pipe(
        concatMap(() =>
          times.length ? etapa('times', this.imports.commitRows(id, 'times', times)) : of(null),
        ),
        concatMap(() =>
          tecnicos.length
            ? etapa('tecnicos', this.imports.commitRows(id, 'tecnicos', tecnicos))
            : of(null),
        ),
        concatMap(() =>
          edicoes.length
            ? etapa(
                'edicoes',
                forkJoin(
                  edicoes
                    .filter(({ tecnico }) => tecnico !== null)
                    .map(({ time, tecnico }) =>
                      this.coaches.update(
                        id,
                        tecnico!.id,
                        {
                          displayName: this.tecnicos()[time.id].trim() || null,
                          priceTier: tecnico!.priceTier,
                          initialPriceOverride: tecnico!.initialPriceOverride,
                        },
                        tecnico!.version,
                      ),
                    ),
                ),
              )
            : of(null),
        ),
      )
      .subscribe({
        complete: () => {
          this.salvando.set(false);
          this.novas.set([]);
          this.tecnicos.set({});
          this.avisar('success', resumo(novos.length, edicoes.length));
          this.store.reload();
        },
        error: (erro: FalhaNaEtapa) => this.tratarFalha(erro, novos, comTecnico),
      });
  }

  private validar(novos: readonly NovoTime[], editados: readonly RealTeam[]): boolean {
    const erros: Record<number, ErroDaLinha> = {};
    const errosDeTecnico: Record<string, string> = {};
    const usados = new Set(this.store.timesAtivos().map((time) => chave(time.name)));

    for (const linha of novos) {
      const nome = linha.nome.trim();
      const tecnico = linha.tecnico.trim();
      const erro: { nome?: string; tecnico?: string } = {};
      if (nome.length < REAL_TEAM_NAME_MIN || nome.length > REAL_TEAM_NAME_MAX) {
        erro.nome = `Use de ${REAL_TEAM_NAME_MIN} a ${REAL_TEAM_NAME_MAX} caracteres.`;
      } else if (usados.has(chave(nome))) {
        erro.nome = 'Já existe um time com esse nome.';
      }
      if (tecnico && (tecnico.length < COACH_NAME_MIN || tecnico.length > COACH_NAME_MAX)) {
        erro.tecnico = `Use de ${COACH_NAME_MIN} a ${COACH_NAME_MAX} caracteres.`;
      }
      usados.add(chave(nome));
      if (erro.nome || erro.tecnico) erros[linha.chave] = erro;
    }

    for (const time of editados) {
      const tecnico = this.tecnicos()[time.id].trim();
      if (tecnico && (tecnico.length < COACH_NAME_MIN || tecnico.length > COACH_NAME_MAX)) {
        errosDeTecnico[time.id] = `Use de ${COACH_NAME_MIN} a ${COACH_NAME_MAX} caracteres.`;
      }
    }

    this.erros.set(erros);
    this.errosDeTecnico.set(errosDeTecnico);
    const valido = Object.keys(erros).length === 0 && Object.keys(errosDeTecnico).length === 0;
    if (!valido) this.avisar('danger', 'Corrija os campos marcados antes de salvar.');
    return valido;
  }

  private tratarFalha(
    erro: FalhaNaEtapa,
    novos: readonly NovoTime[],
    comTecnico: readonly NovoTime[],
  ): void {
    this.salvando.set(false);
    const linhas = erro.etapa === 'times' ? novos : erro.etapa === 'tecnicos' ? comTecnico : [];
    const campo = erro.etapa === 'times' ? 'nome' : 'tecnico';
    const erros: Record<number, ErroDaLinha> = {};
    for (const problema of importIssues(erro.falha)) {
      const indice = rowIndexOf(problema);
      const linha = indice === null ? undefined : linhas[indice];
      if (linha) erros[linha.chave] = { ...erros[linha.chave], [campo]: problema.message };
    }
    this.erros.set(erros);

    if (erro.etapa === 'tecnicos') {
      // Os times entraram; só o técnico deles ficou para trás. Eles passam para a lista dos
      // cadastrados, onde o técnico se corrige — reenviar a linha criaria o time de novo.
      const motivo = importIssues(erro.falha)[0]?.message ?? erro.falha.message;
      this.novas.set([]);
      this.erros.set({});
      this.store.reload();
      this.avisar('warning', `Os times foram salvos, mas o técnico não: ${motivo}`);
      return;
    }
    if (erro.falha.status === 409) {
      this.tecnicos.set({});
      this.store.reload();
      this.avisar('warning', 'Alguém alterou um técnico enquanto você editava. Refaça a mudança.');
      return;
    }
    this.avisar(
      'danger',
      Object.keys(erros).length > 0
        ? 'Nada foi salvo. Corrija as linhas marcadas.'
        : erro.falha.message,
    );
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}

/** Marca a falha com a etapa em que aconteceu, para o erro voltar à linha certa. */
function etapa<T>(nome: FalhaNaEtapa['etapa'], fluxo: Observable<T>): Observable<T> {
  return fluxo.pipe(
    catchError((falha: ApiFailure) => throwError(() => ({ etapa: nome, falha }) as FalhaNaEtapa)),
  );
}

function chave(nome: string): string {
  return nome.trim().toLocaleLowerCase('pt-BR');
}

function resumo(times: number, tecnicos: number): string {
  const partes = [
    times > 0 ? `${times} ${times === 1 ? 'time adicionado' : 'times adicionados'}` : '',
    tecnicos > 0 ? `${tecnicos} ${tecnicos === 1 ? 'técnico alterado' : 'técnicos alterados'}` : '',
  ].filter(Boolean);
  return `${partes.join(' e ')}.`.replace(/^./, (letra) => letra.toLocaleUpperCase('pt-BR'));
}
