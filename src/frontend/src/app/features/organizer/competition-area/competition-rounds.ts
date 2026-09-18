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

import { ApiFailure } from '../../../core/api/problem-details';
import {
  Alert,
  Badge,
  BadgeTone,
  Button,
  Card,
  Dialog,
  FormField,
  Loading,
  SelectField,
  SelectOption,
} from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import { RealTeam, RealTeamService } from './real-team.service';
import {
  MATCH_STATUS_LABELS,
  Match,
  MatchInput,
  MatchStatus,
  PHASE_LABELS,
  ROUND_NAME_MAX,
  ROUND_NAME_MIN,
  ROUND_STATUS_CODE,
  Round,
  RoundPhase,
  RoundService,
  RoundTransition,
} from './round.service';
import { Stage, StageService } from './stage.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | {
      readonly tipo: 'pronto';
      readonly rodadas: readonly Round[];
      readonly fases: readonly Stage[];
      readonly times: readonly RealTeam[];
    }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Formulário de partida aberto dentro de uma rodada. */
interface Formulario {
  readonly rodadaId: string;
  readonly partidaId: string | null;
  readonly stageId: string;
  readonly homeTeamId: string;
  readonly awayTeamId: string;
  readonly kickoffLocal: string;
  readonly status: MatchStatus;
}

/**
 * Rodadas e partidas (02 §9.1, `/organizar/c/:campeonato/rodadas`).
 *
 * A rodada é a janela do fantasy: um conjunto de jogos com um mercado só. O horário é
 * digitado no fuso do campeonato e convertido pelo servidor — a tela nunca faz conta de
 * fuso, senão o resultado dependeria do relógio de quem está olhando.
 */
@Component({
  selector: 'app-competition-rounds',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Dialog, FormField, Loading, RouterLink, SelectField],
  template: `
    <div class="pagina">
      <h1>Rodadas</h1>
      <p class="intro">
        Uma rodada é um conjunto de partidas com um mercado só. Marque os jogos com a rodada em
        rascunho, abra o mercado e confira as súmulas juntas depois dos jogos.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando as rodadas…" /></app-card>
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
          @if (proprietario() && fases().length === 0) {
            <app-card>
              <app-alert tone="warning">
                Crie uma fase e confirme os times dela antes de marcar partidas.
              </app-alert>
            </app-card>
          }

          @for (rodada of rodadas(); track rodada.id) {
            <app-card [heading]="rodada.sequence + '. ' + rodada.name">
              <p class="situacao">
                <app-badge [tone]="tomDaFase(rodada.phase)">{{ fase(rodada.phase) }}</app-badge>
                @if (rodada.marketCloseLocal; as fechamento) {
                  <span>Mercado fecha em {{ quando(fechamento) }}</span>
                }
              </p>

              @if (rodada.matches.length > 0) {
                <ul class="partidas">
                  @for (partida of rodada.matches; track partida.id) {
                    <li class="partida" [class.partida--fora]="partida.status !== 'Scheduled'">
                      <span class="partida__times">
                        {{ partida.homeTeamName }} × {{ partida.awayTeamName }}
                      </span>
                      <span class="partida__detalhe">
                        {{ quando(partida.kickoffLocal) }} · {{ partida.stageName
                        }}{{ partida.groupName ? ' · ' + partida.groupName : '' }}
                      </span>
                      @if (partida.status !== 'Scheduled') {
                        <app-badge tone="neutral">{{ situacao(partida.status) }}</app-badge>
                      }
                      @if (
                        partida.status === 'Scheduled' &&
                        (rodada.phase === 'InProgress' || rodada.phase === 'UnderReview')
                      ) {
                        <a class="acao" [routerLink]="['../partidas', partida.id, 'sumula']">
                          Preencher súmula<span class="sr-only">
                            de {{ partida.homeTeamName }} contra {{ partida.awayTeamName }}</span
                          >
                        </a>
                      }
                      @if (proprietario()) {
                        <span class="partida__acoes">
                          <app-button variant="ghost" (pressed)="abrirPartida(rodada, partida)">
                            Editar<span class="sr-only">
                              {{ partida.homeTeamName }} contra {{ partida.awayTeamName }}</span
                            >
                          </app-button>
                          @if (rodada.status === 'Draft') {
                            <app-button variant="ghost" (pressed)="pedirRemocao(rodada, partida)">
                              Remover<span class="sr-only">
                                {{ partida.homeTeamName }} contra {{ partida.awayTeamName }}</span
                              >
                            </app-button>
                          }
                        </span>
                      }
                    </li>
                  }
                </ul>
              } @else {
                <p class="apoio">Nenhuma partida marcada nesta rodada.</p>
              }

              @if (rodada.phase === 'InProgress' || rodada.phase === 'UnderReview') {
                <p>
                  <a class="acao" [routerLink]="[rodada.id, 'revisao']">
                    Revisar rodada<span class="sr-only"> {{ rodada.name }}</span>
                  </a>
                </p>
              }

              @if (formularioDe(rodada.id); as aberto) {
                <form class="formulario" (submit)="salvarPartida($event, rodada)" novalidate>
                  <app-select-field
                    label="Fase"
                    [options]="opcoesDeFase()"
                    [disabled]="aberto.partidaId !== null && rodada.status !== 'Draft'"
                    [value]="aberto.stageId"
                    (valueChange)="ajustar({ stageId: $event, homeTeamId: '', awayTeamId: '' })"
                  />
                  <app-select-field
                    label="Mandante"
                    [options]="opcoesDeTime(aberto.stageId)"
                    [disabled]="aberto.partidaId !== null && rodada.status !== 'Draft'"
                    [value]="aberto.homeTeamId"
                    (valueChange)="ajustar({ homeTeamId: $event })"
                  />
                  <app-select-field
                    label="Visitante"
                    [options]="opcoesDeTime(aberto.stageId)"
                    [disabled]="aberto.partidaId !== null && rodada.status !== 'Draft'"
                    [value]="aberto.awayTeamId"
                    (valueChange)="ajustar({ awayTeamId: $event })"
                  />
                  <app-form-field
                    label="Data e hora"
                    type="datetime-local"
                    [required]="true"
                    [hint]="'No fuso do campeonato.'"
                    [error]="erroDoHorario()"
                    [value]="aberto.kickoffLocal"
                    (valueChange)="ajustar({ kickoffLocal: $event })"
                  />
                  @if (aberto.partidaId !== null) {
                    <app-select-field
                      label="Situação"
                      [options]="opcoesDeSituacao"
                      [value]="aberto.status"
                      (valueChange)="ajustar({ status: $any($event) })"
                    />
                  }
                  <div class="acoes">
                    <app-button type="submit" [loading]="salvando()">
                      {{ aberto.partidaId === null ? 'Adicionar partida' : 'Salvar partida' }}
                    </app-button>
                    <app-button
                      variant="ghost"
                      [disabled]="salvando()"
                      (pressed)="fecharFormulario()"
                    >
                      Cancelar
                    </app-button>
                  </div>
                </form>
              } @else if (proprietario()) {
                <div class="acoes">
                  @if (rodada.status === 'Draft') {
                    <app-button
                      variant="secondary"
                      [disabled]="fases().length === 0"
                      (pressed)="abrirPartida(rodada, null)"
                    >
                      Adicionar partida<span class="sr-only"> em {{ rodada.name }}</span>
                    </app-button>
                    <app-button
                      [disabled]="rodada.matches.length === 0"
                      (pressed)="transicao(rodada, 'OpenMarket')"
                    >
                      Abrir mercado<span class="sr-only"> de {{ rodada.name }}</span>
                    </app-button>
                  } @else if (rodada.phase === 'MarketOpen') {
                    <app-button
                      variant="secondary"
                      (pressed)="transicao(rodada, 'ReopenForEditing')"
                    >
                      Voltar para rascunho<span class="sr-only"> em {{ rodada.name }}</span>
                    </app-button>
                  }
                  @if (rodada.status === 'Draft' || rodada.status === 'MarketOpen') {
                    <app-button variant="ghost" (pressed)="pedirCancelamento(rodada)">
                      Cancelar rodada<span class="sr-only">{{ ' ' + rodada.name }}</span>
                    </app-button>
                  }
                </div>
              }
            </app-card>
          } @empty {
            <app-card>
              <p class="apoio">
                {{
                  proprietario()
                    ? 'Nenhuma rodada ainda. Crie a primeira para marcar os jogos.'
                    : 'Nenhuma rodada ainda.'
                }}
              </p>
            </app-card>
          }

          @if (proprietario()) {
            @if (criando()) {
              <app-card heading="Nova rodada">
                <form (submit)="criar($event)" novalidate>
                  <app-form-field
                    label="Nome da rodada"
                    placeholder="Rodada 1"
                    [required]="true"
                    [maxLength]="nomeMax"
                    [error]="erroDoNome()"
                    [(value)]="nome"
                  />
                  <div class="acoes">
                    <app-button type="submit" [loading]="salvando()">Adicionar rodada</app-button>
                    <app-button
                      variant="ghost"
                      [disabled]="salvando()"
                      (pressed)="cancelarCriacao()"
                    >
                      Cancelar
                    </app-button>
                  </div>
                </form>
              </app-card>
            } @else {
              <div class="acoes">
                <app-button (pressed)="abrirNova()">Adicionar rodada</app-button>
              </div>
            }
          }
        }
      }
    </div>

    @if (proprietario()) {
      <app-dialog
        [heading]="'Cancelar ' + (cancelando()?.name ?? '') + '?'"
        [open]="cancelando() !== null"
        (dismissed)="fecharCancelamento()"
      >
        <p>A rodada sai do campeonato e não volta atrás. As partidas dela ficam sem efeito.</p>
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="fecharCancelamento()">
            Voltar
          </app-button>
          <app-button variant="danger" [loading]="salvando()" (pressed)="confirmarCancelamento()">
            Cancelar rodada
          </app-button>
        </div>
      </app-dialog>

      <app-dialog
        heading="Remover a partida?"
        [open]="removendo() !== null"
        (dismissed)="fecharRemocao()"
      >
        <p>
          {{ removendo()?.partida?.homeTeamName }} × {{ removendo()?.partida?.awayTeamName }} sai
          desta rodada.
        </p>
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="fecharRemocao()">
            Voltar
          </app-button>
          <app-button variant="danger" [loading]="salvando()" (pressed)="confirmarRemocao()">
            Remover partida
          </app-button>
        </div>
      </app-dialog>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-rounds.scss'],
})
export class CompetitionRoundsPage {
  private readonly service = inject(RoundService);
  private readonly stages = inject(StageService);
  private readonly teams = inject(RealTeamService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly nomeMax = ROUND_NAME_MAX;
  protected readonly opcoesDeSituacao: readonly SelectOption[] = (
    ['Scheduled', 'Postponed', 'Cancelled'] as const
  ).map((value) => ({ value, label: MATCH_STATUS_LABELS[value] }));

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly criando = signal(false);
  protected readonly nome = signal('');
  protected readonly erroDoNome = signal<string | undefined>(undefined);
  protected readonly erroDoHorario = signal<string | undefined>(undefined);
  protected readonly salvando = signal(false);
  protected readonly formulario = signal<Formulario | null>(null);
  protected readonly cancelando = signal<Round | null>(null);
  protected readonly removendo = signal<{ rodada: Round; partida: Match } | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly rodadas = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.rodadas : [];
  });

  protected readonly fases = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.fases : [];
  });

  protected readonly opcoesDeFase = computed<readonly SelectOption[]>(() =>
    this.fases().map((fase) => ({ value: fase.id, label: `${fase.sequence}. ${fase.name}` })),
  );

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

  protected fase(phase: RoundPhase): string {
    return PHASE_LABELS[phase];
  }

  protected situacao(status: MatchStatus): string {
    return MATCH_STATUS_LABELS[status];
  }

  protected tomDaFase(phase: RoundPhase): BadgeTone {
    if (phase === 'Cancelled') return 'danger';
    if (phase === 'Draft') return 'warning';
    return phase === 'MarketOpen' ? 'success' : 'neutral';
  }

  /** `2026-09-20T15:30` vira `20/09/2026 15:30`, sem conversão de fuso. */
  protected quando(local: string): string {
    const [data, hora] = local.split('T');
    const [ano, mes, dia] = data.split('-');
    return `${dia}/${mes}/${ano} ${hora}`;
  }

  /** Só times confirmados na fase escolhida podem jogar (o servidor recusa o resto). */
  protected opcoesDeTime(stageId: string): readonly SelectOption[] {
    const fase = this.fases().find((item) => item.id === stageId);
    if (!fase) return [];
    return fase.participants.map((participante) => ({
      value: participante.realTeamId,
      label: participante.stageGroupName
        ? `${participante.realTeamName} — ${participante.stageGroupName}`
        : participante.realTeamName,
    }));
  }

  protected formularioDe(rodadaId: string): Formulario | null {
    const aberto = this.formulario();
    return aberto?.rodadaId === rodadaId ? aberto : null;
  }

  protected carregar(): void {
    if (this.estado().tipo !== 'pronto') {
      this.estado.set({ tipo: 'carregando' });
    }

    const id = this.campeonatoId;
    this.service.list(id).subscribe({
      next: (rodadas) =>
        this.stages.list(id).subscribe({
          next: (fases) =>
            this.teams.list(id).subscribe({
              next: (times) => this.estado.set({ tipo: 'pronto', rodadas, fases, times }),
              error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
            }),
          error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
        }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrirNova(): void {
    this.retorno.set(null);
    this.nome.set('');
    this.erroDoNome.set(undefined);
    this.criando.set(true);
  }

  protected cancelarCriacao(): void {
    this.criando.set(false);
    this.erroDoNome.set(undefined);
  }

  protected criar(event: Event): void {
    event.preventDefault();
    const nome = this.nome().trim();
    if (nome.length < ROUND_NAME_MIN || nome.length > ROUND_NAME_MAX) {
      this.erroDoNome.set(`Use de ${ROUND_NAME_MIN} a ${ROUND_NAME_MAX} caracteres.`);
      return;
    }

    this.salvando.set(true);
    this.service.create(this.campeonatoId, nome).subscribe({
      next: (rodada) => {
        this.criando.set(false);
        this.concluir(`${rodada.name} criada.`);
      },
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected abrirPartida(rodada: Round, partida: Match | null): void {
    this.retorno.set(null);
    this.erroDoHorario.set(undefined);
    const primeiraFase = this.fases()[0];
    this.formulario.set({
      rodadaId: rodada.id,
      partidaId: partida?.id ?? null,
      stageId: partida?.stageId ?? primeiraFase?.id ?? '',
      homeTeamId: partida?.homeTeamId ?? '',
      awayTeamId: partida?.awayTeamId ?? '',
      kickoffLocal: partida?.kickoffLocal ?? '',
      status: partida?.status ?? 'Scheduled',
    });
  }

  protected ajustar(mudanca: Partial<Formulario>): void {
    const atual = this.formulario();
    if (atual) this.formulario.set({ ...atual, ...mudanca });
  }

  protected fecharFormulario(): void {
    this.formulario.set(null);
    this.erroDoHorario.set(undefined);
  }

  protected salvarPartida(event: Event, rodada: Round): void {
    event.preventDefault();
    const aberto = this.formulario();
    if (!aberto || this.salvando()) return;

    if (!aberto.kickoffLocal) {
      this.erroDoHorario.set('Informe a data e a hora do jogo.');
      return;
    }

    const entrada: MatchInput = {
      stageId: aberto.stageId,
      homeTeamId: aberto.homeTeamId,
      awayTeamId: aberto.awayTeamId,
      kickoffLocal: aberto.kickoffLocal,
      status: aberto.status,
    };
    this.salvando.set(true);
    const pedido =
      aberto.partidaId === null
        ? this.service.addMatch(this.campeonatoId, rodada.id, entrada, rodada.version)
        : this.service.updateMatch(
            this.campeonatoId,
            rodada.id,
            aberto.partidaId,
            entrada,
            rodada.version,
          );

    pedido.subscribe({
      next: () => {
        this.fecharFormulario();
        this.concluir(aberto.partidaId === null ? 'Partida adicionada.' : 'Partida salva.');
      },
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected transicao(rodada: Round, transicao: RoundTransition): void {
    if (this.salvando()) return;
    this.salvando.set(true);
    this.service.changeStatus(this.campeonatoId, rodada.id, transicao, rodada.version).subscribe({
      next: () =>
        this.concluir(
          transicao === 'OpenMarket'
            ? `Mercado de ${rodada.name} aberto.`
            : `${rodada.name} voltou para rascunho.`,
        ),
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  protected pedirCancelamento(rodada: Round): void {
    this.cancelando.set(rodada);
  }

  protected fecharCancelamento(): void {
    if (!this.salvando()) this.cancelando.set(null);
  }

  protected confirmarCancelamento(): void {
    const rodada = this.cancelando();
    if (!rodada || this.salvando()) return;

    this.salvando.set(true);
    this.service.changeStatus(this.campeonatoId, rodada.id, 'Cancel', rodada.version).subscribe({
      next: () => {
        this.cancelando.set(null);
        this.concluir(`${rodada.name} cancelada.`);
      },
      error: (falha: ApiFailure) => {
        this.cancelando.set(null);
        this.tratarFalha(falha);
      },
    });
  }

  protected pedirRemocao(rodada: Round, partida: Match): void {
    this.removendo.set({ rodada, partida });
  }

  protected fecharRemocao(): void {
    if (!this.salvando()) this.removendo.set(null);
  }

  protected confirmarRemocao(): void {
    const alvo = this.removendo();
    if (!alvo || this.salvando()) return;

    this.salvando.set(true);
    this.service
      .removeMatch(this.campeonatoId, alvo.rodada.id, alvo.partida.id, alvo.rodada.version)
      .subscribe({
        next: () => {
          this.removendo.set(null);
          this.concluir('Partida removida.');
        },
        error: (falha: ApiFailure) => {
          this.removendo.set(null);
          this.tratarFalha(falha);
        },
      });
  }

  private concluir(texto: string): void {
    this.salvando.set(false);
    this.avisar('success', texto);
    this.carregar();
  }

  private tratarFalha(falha: ApiFailure): void {
    this.salvando.set(false);
    const validacao = primeiroErro(falha);
    if (validacao) {
      this.avisar('danger', validacao);
      this.carregar();
      return;
    }

    if (falha.code === ROUND_STATUS_CODE) {
      this.fecharFormulario();
      this.avisar('warning', 'A rodada mudou de situação. A lista foi atualizada.');
      this.carregar();
      return;
    }

    if (falha.status === 409) {
      this.fecharFormulario();
      this.avisar(
        'warning',
        'Esta rodada foi alterada por outra pessoa. A lista foi atualizada; refaça sua mudança.',
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

/** A primeira mensagem de validação do servidor, que é a que explica o que houve. */
function primeiroErro(falha: ApiFailure): string | null {
  const errors = falha.problem?.['errors'];
  if (typeof errors !== 'object' || errors === null) return null;
  for (const mensagens of Object.values(errors as Record<string, unknown>)) {
    if (Array.isArray(mensagens) && typeof mensagens[0] === 'string') {
      return mensagens[0];
    }
  }

  return null;
}
