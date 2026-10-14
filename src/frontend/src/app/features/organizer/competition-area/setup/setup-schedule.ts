import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { concatMap, from, toArray } from 'rxjs';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Alert, Button, Card, Dialog } from '../../../../shared/ui';
import { ImportRow, ImportService, importIssues } from '../import.service';
import { RoundService } from '../round.service';
import { Stage, StageService } from '../stage.service';
import { SetupFooter } from './competition-setup';
import {
  Legs,
  PlannedRound,
  ProposedRound,
  addDays,
  groupRounds,
  knockoutDraw,
  knockoutRounds,
  knockoutStageSize,
  nextRoundNames,
  schedule,
  shuffle,
  uniqueName,
} from './fixture-generator';
import { SetupStore } from './setup-store';

const DIAS = new Intl.DateTimeFormat('pt-BR', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
  timeZone: 'UTC',
});

/**
 * Passo 4: a tabela de jogos, gerada a partir do formato. A pessoa escolhe turno ou
 * returno (ida e volta no mata-mata), o dia da primeira rodada e os horários; a prévia
 * mostra rodada por rodada, e salvar cria tudo em rascunho pela importação de partidas,
 * que confere fase, times e grupos como o cadastro pela tela. No mata-mata, a fase
 * seguinte só existe depois que a organização confirma quem passou.
 */
@Component({
  selector: 'app-setup-schedule',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, Dialog, RouterLink, SetupFooter],
  template: `
    <section class="etapa" aria-labelledby="passo-tabela">
      <h2 id="passo-tabela">4. Tabela de jogos</h2>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">
            {{ resultado.texto }}
            @for (detalhe of resultado.detalhes ?? []; track $index) {
              <br />{{ detalhe }}
            }
          </app-alert>
        }
      </div>

      @if (fase(); as alvo) {
        <p class="intro">
          Gerando os jogos de <strong>{{ alvo.name }}</strong> ({{ alvo.participants.length }}
          times). Confira a prévia e salve: as rodadas nascem em rascunho, e dá para remarcar
          qualquer jogo depois em Rodadas.
        </p>

        <app-card heading="Como e quando">
          <div class="opcoes">
            <label class="grade__celula">
              {{ alvo.format === 'Knockout' ? 'Confronto' : 'Turnos' }}
              <select class="campo" (change)="turnos.set(numero($event) === 2 ? 2 : 1)">
                <option value="1" [selected]="turnos() === 1">
                  {{ alvo.format === 'Knockout' ? 'Jogo único' : 'Turno único' }}
                </option>
                <option value="2" [selected]="turnos() === 2">
                  {{ alvo.format === 'Knockout' ? 'Ida e volta' : 'Turno e returno' }}
                </option>
              </select>
            </label>
            <label class="grade__celula">
              Primeira rodada
              <input
                class="campo"
                type="date"
                required
                [value]="data()"
                (change)="data.set(texto($event))"
              />
            </label>
            <label class="grade__celula">
              Uma rodada a cada
              <select class="campo" (change)="intervalo.set(numero($event))">
                @for (opcao of intervalos; track opcao.dias) {
                  <option [value]="opcao.dias" [selected]="opcao.dias === intervalo()">
                    {{ opcao.texto }}
                  </option>
                }
              </select>
            </label>
            <label class="grade__celula">
              Primeiro jogo do dia
              <input
                class="campo"
                type="time"
                required
                [value]="horario()"
                (change)="horario.set(texto($event))"
              />
            </label>
            <label class="grade__celula">
              Um jogo a cada (minutos)
              <input
                class="campo"
                type="number"
                min="20"
                max="240"
                step="5"
                [value]="duracao()"
                (change)="duracao.set(numero($event))"
              />
            </label>
            <label class="grade__celula">
              Campos ao mesmo tempo
              <input
                class="campo"
                type="number"
                min="1"
                max="8"
                [value]="campos()"
                (change)="campos.set(numero($event))"
              />
            </label>
          </div>
        </app-card>

        @if (alvo.format === 'Knockout') {
          <app-card heading="Ordem da chave">
            <p class="dica">
              O primeiro enfrenta o último, o segundo o penúltimo, e o de cima é o mandante. Use a
              ordem de campanha, ou sorteie.
              @if (chave().byes.length > 0) {
                {{ ' ' + chave().byes.length }} passam direto para a fase seguinte: os primeiros da
                lista.
              }
            </p>
            <div class="acoes">
              <app-button variant="secondary" (pressed)="sortearChave()">Sortear</app-button>
            </div>
            <ol class="ordem">
              @for (nome of sementes(); track nome; let posicao = $index; let ultimo = $last) {
                <li class="ordem__item">
                  <span class="ordem__nome">
                    {{ nome }}
                    @if (chave().byes.includes(nome)) {
                      <span class="dica">· passa direto</span>
                    }
                  </span>
                  <!-- Sem botão onde não há para onde mover: desabilitado, ele parecia marcado. -->
                  @if (posicao > 0) {
                    <app-button variant="ghost" size="compact" (pressed)="mover(posicao, -1)">
                      Subir<span class="sr-only"> {{ nome }}</span>
                    </app-button>
                  } @else {
                    <span class="ordem__vago" aria-hidden="true"></span>
                  }
                  @if (!ultimo) {
                    <app-button variant="ghost" size="compact" (pressed)="mover(posicao, 1)">
                      Descer<span class="sr-only"> {{ nome }}</span>
                    </app-button>
                  } @else {
                    <span class="ordem__vago" aria-hidden="true"></span>
                  }
                </li>
              }
            </ol>
          </app-card>
        }

        @if (proposta(); as rodadas) {
          <app-card
            [heading]="
              'Prévia: ' +
              contagem(totalDeJogos(), 'jogo', 'jogos') +
              ' em ' +
              contagem(rodadas.length, 'rodada', 'rodadas')
            "
          >
            <div class="previa">
              @for (rodada of rodadas; track rodada.name) {
                <section class="previa__rodada" [attr.aria-label]="rodada.name">
                  <h3>
                    {{ rodada.name }}
                    <span class="dica">{{ dia(rodada.date) }}</span>
                  </h3>
                  <ul class="previa__jogos">
                    @for (jogo of rodada.matches; track $index) {
                      <li>
                        <span class="previa__hora">{{ jogo.time }}</span>
                        {{ jogo.home }} <span aria-hidden="true">×</span
                        ><span class="sr-only"> contra </span> {{ jogo.away }}
                        @if (jogo.group) {
                          <span class="dica">· {{ jogo.group }}</span>
                        }
                      </li>
                    }
                  </ul>
                </section>
              }
            </div>
          </app-card>

          <div class="acoes">
            <app-button escrita [loading]="salvando()" (pressed)="salvar(alvo, rodadas)">
              Salvar a tabela de {{ alvo.name }}
            </app-button>
          </div>
        } @else {
          <app-alert tone="warning">
            Os jogos de uma rodada não cabem no dia. Comece mais cedo, diminua o tempo entre os
            jogos ou use mais campos.
          </app-alert>
        }
      } @else if (proximaFase(); as proxima) {
        <p class="intro">
          A tabela das fases anteriores está pronta. Quando
          {{ faseAnterior()?.name ?? 'a fase anterior' }} terminar, confirme quem passou para
          <strong>{{ proxima.name }}</strong> e gere os confrontos.
        </p>

        <app-card [heading]="'Quem vai para ' + proxima.name">
          <p class="dica">
            Marque os classificados na ordem de campanha: a ordem vira a chave, e dá para ajustar
            depois.
            @if (vagas(proxima); as total) {
              {{ ' ' + proxima.name }} recebe {{ total }} times.
            }
          </p>
          <ul class="classificados">
            @for (participante of elegiveis(); track participante.id) {
              <li>
                <label class="classificado">
                  <input
                    type="checkbox"
                    [checked]="escolhidos().includes(participante.id)"
                    (change)="alternar(participante.id)"
                  />
                  <span class="classificado__texto">
                    <span class="classificado__nome">{{ participante.nome }}</span>
                    @if (participante.grupo) {
                      <span class="dica">{{ participante.grupo }}</span>
                    }
                  </span>
                  @let ordemDele = escolhidos().indexOf(participante.id);
                  @if (ordemDele >= 0) {
                    <span class="classificado__ordem">{{ ordemDele + 1 }}º</span>
                  }
                </label>
              </li>
            }
          </ul>
          <div class="acoes">
            <app-button
              escrita
              [disabled]="escolhidos().length < 2"
              [loading]="salvando()"
              (pressed)="confirmar(proxima)"
            >
              Confirmar {{ escolhidos().length }} em {{ proxima.name }}
            </app-button>
          </div>
        </app-card>
      } @else if ((store.dados()?.stages ?? []).length === 0) {
        <p class="intro">A tabela sai do formato do campeonato.</p>
        <app-card>
          <p class="apoio">Escolha o formato antes de gerar os jogos.</p>
          <a class="acao" routerLink="../formato">Escolher o formato</a>
        </app-card>
      } @else {
        <p class="intro">
          Todos os jogos que dá para marcar agora estão na tabela: {{ store.jogos().length }} jogos.
          Remarque, adie ou abra o mercado de cada rodada em Rodadas.
        </p>
      }

      @if (store.jogos().length > 0) {
        <div class="acoes">
          <a class="acao" routerLink="../../rodadas">Ver em Rodadas</a>
          @if (rascunhos().length > 0) {
            <app-button escrita variant="destructive" (pressed)="apagando.set(true)">
              Apagar as {{ rascunhos().length }} rodadas em rascunho
            </app-button>
          }
        </div>
      }

      <app-setup-footer passo="tabela" />
    </section>

    <app-dialog
      [heading]="'Apagar ' + rascunhos().length + ' rodadas em rascunho?'"
      [open]="apagando()"
      (dismissed)="apagando.set(false)"
    >
      <p>
        As rodadas que ainda não abriram mercado são apagadas com os jogos delas, para gerar a
        tabela de novo. Rodadas com mercado aberto ou resultado ficam como estão.
      </p>
      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="salvando()" (pressed)="apagando.set(false)">
          Cancelar
        </app-button>
        <app-button escrita variant="danger" [loading]="salvando()" (pressed)="apagarRascunhos()">
          Apagar as rodadas
        </app-button>
      </div>
    </app-dialog>
  `,
  styleUrls: [
    '../../organizer.scss',
    '../competition-pages.scss',
    './setup.scss',
    './setup-format.scss',
    './setup-schedule.scss',
  ],
})
export class SetupScheduleStep {
  protected readonly store = inject(SetupStore);
  private readonly imports = inject(ImportService);
  private readonly stages = inject(StageService);
  private readonly rounds = inject(RoundService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly intervalos = [
    { dias: 7, texto: 'semana' },
    { dias: 14, texto: 'duas semanas' },
    { dias: 1, texto: 'dia' },
    { dias: 3, texto: 'três dias' },
  ];

  protected readonly turnos = signal<Legs>(1);
  protected readonly data = signal(this.primeiraData());
  protected readonly intervalo = signal(7);
  protected readonly horario = signal('09:00');
  protected readonly duracao = signal(60);
  protected readonly campos = signal(1);
  /** Ordem da chave do mata-mata, pelo nome do time; nula usa a ordem da fase. */
  private readonly ordem = signal<readonly string[] | null>(null);
  /** A fase que a tela já preparou, para só reiniciar as escolhas quando ela muda. */
  private faseVista: string | null = null;
  protected readonly escolhidos = signal<readonly string[]>([]);
  protected readonly salvando = signal(false);
  protected readonly apagando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
    readonly detalhes?: readonly string[];
  } | null>(null);

  private readonly fases = computed(() =>
    [...(this.store.dados()?.stages ?? [])].sort((a, b) => a.sequence - b.sequence),
  );

  /** A primeira fase com times confirmados e nenhum jogo: é a que a tabela gera agora. */
  protected readonly fase = computed(
    () =>
      this.fases().find(
        (fase) =>
          fase.participants.length >= 2 &&
          !this.store.jogos().some((jogo) => jogo.stageId === fase.id),
      ) ?? null,
  );

  /** Fase sem times depois de uma que já tem: a próxima do mata-mata. */
  protected readonly proximaFase = computed(() => {
    const fases = this.fases();
    const indice = fases.findIndex(
      (fase, posicao) => posicao > 0 && fase.participants.length === 0,
    );
    return indice > 0 && fases[indice - 1].participants.length > 0 ? fases[indice] : null;
  });

  protected readonly faseAnterior = computed(() => {
    const proxima = this.proximaFase();
    const fases = this.fases();
    return proxima ? (fases[fases.indexOf(proxima) - 1] ?? null) : null;
  });

  /** Quem pode passar: os times da fase anterior, com o grupo para ajudar a escolher. */
  protected readonly elegiveis = computed(() =>
    (this.faseAnterior()?.participants ?? [])
      .filter((participante) => !participante.isTeamArchived)
      .map((participante) => ({
        id: participante.realTeamId,
        nome: participante.realTeamName,
        grupo: (this.faseAnterior()?.groups.length ?? 0) > 1 ? participante.stageGroupName : null,
      })),
  );

  protected readonly sementes = computed(
    () => this.ordem() ?? this.fase()?.participants.map((p) => p.realTeamName) ?? [],
  );

  protected readonly chave = computed(() => knockoutDraw(this.sementes()));

  private readonly planejadas = computed<PlannedRound[]>(() => {
    const fase = this.fase();
    if (!fase) return [];
    const existentes = (this.store.dados()?.rounds ?? []).map((rodada) => rodada.name);

    if (fase.format === 'Knockout') {
      const rodadas = knockoutRounds(fase.name, this.chave(), this.turnos());
      const usados = [...existentes];
      return rodadas.map((rodada) => {
        const nome = uniqueName(rodada.name, usados);
        usados.push(nome);
        return { ...rodada, name: nome };
      });
    }

    const varios = fase.groups.length > 1;
    const jogos = groupRounds(
      fase.groups.map((grupo) => ({
        name: varios ? grupo.name : null,
        teams: fase.participants
          .filter((p) => p.stageGroupId === grupo.id && !p.isTeamArchived)
          .map((p) => p.realTeamName),
      })),
      this.turnos(),
    );
    const nomes = nextRoundNames(jogos.length, existentes);
    return jogos.map((partidas, indice) => ({ name: nomes[indice], matches: partidas }));
  });

  protected readonly proposta = computed<ProposedRound[] | null>(() =>
    schedule(this.planejadas(), {
      firstDate: this.data(),
      daysBetweenRounds: this.intervalo(),
      firstKickoff: this.horario() || '09:00',
      minutesPerMatch: Math.max(20, this.duracao() || 60),
      fields: Math.max(1, this.campos() || 1),
    }),
  );

  protected readonly totalDeJogos = computed(
    () => this.proposta()?.reduce((total, rodada) => total + rodada.matches.length, 0) ?? 0,
  );

  protected readonly rascunhos = computed(() =>
    (this.store.dados()?.rounds ?? []).filter((rodada) => rodada.status === 'Draft'),
  );

  constructor() {
    // Cada fase gerada começa do zero: a data vem depois do último jogo marcado, e o
    // returno de uma fase de grupos não vira ida e volta no mata-mata sem ninguém pedir.
    effect(() => {
      const fase = this.fase()?.id;
      if (fase === undefined || fase === this.faseVista) return;
      this.faseVista = fase;
      untracked(() => {
        this.data.set(this.primeiraData());
        this.turnos.set(1);
      });
    });
  }

  protected contagem(total: number, singular: string, plural: string): string {
    return `${total} ${total === 1 ? singular : plural}`;
  }

  protected texto(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected numero(event: Event): number {
    return Number((event.target as HTMLInputElement | HTMLSelectElement).value);
  }

  protected dia(data: string): string {
    return DIAS.format(new Date(`${data}T12:00:00Z`));
  }

  protected vagas(fase: Stage): number | null {
    return knockoutStageSize(fase.name);
  }

  protected sortearChave(): void {
    this.ordem.set(shuffle(this.sementes()));
  }

  protected mover(posicao: number, direcao: -1 | 1): void {
    const lista = [...this.sementes()];
    const destino = posicao + direcao;
    if (destino < 0 || destino >= lista.length) return;
    [lista[posicao], lista[destino]] = [lista[destino], lista[posicao]];
    this.ordem.set(lista);
  }

  protected alternar(teamId: string): void {
    this.escolhidos.update((atual) =>
      atual.includes(teamId) ? atual.filter((id) => id !== teamId) : [...atual, teamId],
    );
  }

  protected confirmar(fase: Stage): void {
    if (this.salvando()) return;
    const escolhidos = this.escolhidos();
    const nomes = new Map(this.elegiveis().map((time) => [time.id, time.nome]));
    this.salvando.set(true);
    this.stages
      .setParticipants(
        this.store.campeonatoId(),
        fase.id,
        escolhidos.map((id) => ({ realTeamId: id, stageGroupId: null })),
        fase.version,
      )
      .subscribe({
        next: () => {
          this.salvando.set(false);
          this.escolhidos.set([]);
          // A ordem em que foram marcados vira a chave.
          this.ordem.set(escolhidos.map((id) => nomes.get(id) ?? ''));
          this.avisar('success', `${escolhidos.length} times confirmados em ${fase.name}.`);
          this.store.reload();
        },
        error: (falha: ApiFailure) => {
          this.salvando.set(false);
          this.avisar('danger', falha.message);
        },
      });
  }

  protected salvar(fase: Stage, rodadas: readonly ProposedRound[]): void {
    if (this.salvando()) return;
    const linhas: ImportRow[] = rodadas.flatMap((rodada) =>
      rodada.matches.map((jogo) => ({
        rodada: rodada.name,
        fase: fase.name,
        mandante: jogo.home,
        visitante: jogo.away,
        data: jogo.date.split('-').reverse().join('/'),
        hora: jogo.time,
      })),
    );

    this.salvando.set(true);
    this.imports.commitRows(this.store.campeonatoId(), 'partidas', linhas).subscribe({
      next: (resultado) => {
        this.salvando.set(false);
        this.ordem.set(null);
        this.avisar(
          'success',
          `${this.contagem(resultado.summary.created, 'jogo criado', 'jogos criados')} em ` +
            `${this.contagem(rodadas.length, 'rodada', 'rodadas')}, em rascunho. ` +
            'Abra o mercado de cada rodada em Rodadas quando for a hora.',
        );
        this.store.reload();
      },
      error: (falha: ApiFailure) => {
        this.salvando.set(false);
        const problemas = importIssues(falha).map((problema) => problema.message);
        this.avisar(
          'danger',
          problemas.length > 0 ? 'Nada foi salvo:' : falha.message,
          problemas.slice(0, 5),
        );
      },
    });
  }

  protected apagarRascunhos(): void {
    if (this.salvando()) return;
    const id = this.store.campeonatoId();
    const rodadas = [...this.rascunhos()].sort((a, b) => b.sequence - a.sequence);
    this.salvando.set(true);
    from(rodadas)
      .pipe(
        concatMap((rodada) => this.rounds.remove(id, rodada.id)),
        toArray(),
      )
      .subscribe({
        next: () => {
          this.salvando.set(false);
          this.apagando.set(false);
          this.avisar('success', `${rodadas.length} rodadas apagadas. Gere a tabela de novo.`);
          this.store.reload();
        },
        error: (falha: ApiFailure) => {
          this.salvando.set(false);
          this.apagando.set(false);
          this.store.reload();
          this.avisar('danger', falha.message);
        },
      });
  }

  /** Uma semana depois do último jogo marcado; sem jogo, o próximo domingo. */
  private primeiraData(): string {
    const datas = this.store
      .jogos()
      .map((jogo) => jogo.kickoffLocal.slice(0, 10))
      .sort();
    if (datas.length > 0) return addDays(datas.at(-1)!, 7);

    const hoje = new Date();
    const local = `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, '0')}-${String(
      hoje.getDate(),
    ).padStart(2, '0')}`;
    return addDays(local, 7 - hoje.getDay());
  }

  private avisar(
    tom: 'success' | 'warning' | 'danger',
    texto: string,
    detalhes?: readonly string[],
  ): void {
    this.retorno.set({ tom, texto, detalhes });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
