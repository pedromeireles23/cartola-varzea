import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { ApiFailure } from '../../../core/api/problem-details';
import {
  Alert,
  BackLink,
  Button,
  Card,
  Loading,
  SelectField,
  SelectOption,
} from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  MatchSheet,
  MatchSheetAthlete,
  MatchSheetService,
  RedCardReason,
} from './match-sheet.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto' }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

type CampoNumerico =
  | 'goalsConceded'
  | 'goals'
  | 'assists'
  | 'goalkeeperSaves'
  | 'penaltySaves'
  | 'yellowCards'
  | 'redCards'
  | 'ownGoals'
  | 'penaltyMisses';

@Component({
  selector: 'app-match-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Button, Card, Loading, SelectField],
  template: `
    <div class="pagina">
      <app-back-link link="../rodadas" label="Rodadas" />
      <h1>Súmula da partida</h1>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando a súmula…" /></app-card>
        }
        @case ('erro') {
          <app-card>
            <app-alert tone="danger">{{ falha()?.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          </app-card>
        }
        @case ('pronto') {
          <app-card [heading]="titulo()">
            <p class="apoio">
              {{ sumula()!.roundName }} · informe somente o que aconteceu em campo.
            </p>
            <ol class="etapas" aria-label="Etapas da súmula">
              @for (nome of nomesDasEtapas; track $index) {
                <li>
                  <button
                    type="button"
                    [class.ativa]="etapa() === $index"
                    (click)="etapa.set($index)"
                  >
                    {{ $index + 1 }}. {{ nome }}
                  </button>
                </li>
              }
            </ol>
          </app-card>

          @if (retorno(); as mensagem) {
            <app-alert [tone]="mensagem.tom">{{ mensagem.texto }}</app-alert>
          }

          @if (etapa() === 0) {
            <app-card heading="Placar final">
              <div class="placar">
                <label
                  >{{ sumula()!.homeTeamName
                  }}<input
                    type="number"
                    min="0"
                    max="99"
                    [value]="mandante()"
                    (input)="alterarPlacar(true, $event)"
                /></label>
                <span>×</span>
                <label
                  >{{ sumula()!.awayTeamName
                  }}<input
                    type="number"
                    min="0"
                    max="99"
                    [value]="visitante()"
                    (input)="alterarPlacar(false, $event)"
                /></label>
              </div>
            </app-card>
          }

          @if (etapa() === 1) {
            <app-card heading="Quem entrou em campo">
              <p class="apoio">
                Marque quem jogou e quem atuou no gol. Cada time precisa de um goleiro.
              </p>
              @for (time of times(); track time.id) {
                <h2>{{ time.nome }}</h2>
                <ul class="elenco">
                  @for (atleta of atletasDoTime(time.id); track atleta.athleteId) {
                    <li>
                      <strong>{{ atleta.sportingName }}</strong
                      ><span>{{ posicao(atleta.position) }}</span>
                      <label
                        ><input
                          type="checkbox"
                          [checked]="atleta.didPlay"
                          (change)="alternar(atleta, 'didPlay', $event)"
                        />
                        Jogou</label
                      >
                      <label
                        ><input
                          type="checkbox"
                          [checked]="atleta.playedAsGoalkeeper"
                          [disabled]="!atleta.didPlay"
                          (change)="alternar(atleta, 'playedAsGoalkeeper', $event)"
                        />
                        Atuou no gol</label
                      >
                    </li>
                  }
                </ul>
              }
            </app-card>
          }

          @if (etapa() === 2) {
            <app-card heading="Eventos objetivos">
              <p class="apoio">
                Use quantidades. Gols sofridos aparecem apenas para quem atuou no gol.
              </p>
              <div class="tabela">
                <table>
                  <thead>
                    <tr>
                      <th>Atleta</th>
                      <th>Gols</th>
                      <th>Assist.</th>
                      <th>Defesas</th>
                      <th>Pênaltis def.</th>
                      <th>Amarelos</th>
                      <th>Vermelho</th>
                      <th>Gol contra</th>
                      <th>Pênalti perdido</th>
                      <th>Gols sofridos</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (atleta of participantes(); track atleta.athleteId) {
                      <tr>
                        <th>{{ atleta.sportingName }}</th>
                        @for (campo of camposDeEvento; track campo) {
                          <td>
                            <input
                              class="numero"
                              type="number"
                              min="0"
                              [max]="maximo(campo)"
                              [value]="atleta[campo]"
                              (input)="alterarNumero(atleta, campo, $event)"
                            />
                          </td>
                        }
                        <td>
                          @if (atleta.playedAsGoalkeeper) {
                            <input
                              class="numero"
                              type="number"
                              min="0"
                              max="99"
                              [value]="atleta.goalsConceded"
                              (input)="alterarNumero(atleta, 'goalsConceded', $event)"
                            />
                          } @else {
                            —
                          }
                        </td>
                      </tr>
                      @if (atleta.redCards > 0) {
                        <tr>
                          <td colspan="10">
                            <app-select-field
                              label="Motivo da expulsão de {{ atleta.sportingName }}"
                              [options]="motivos"
                              [value]="atleta.redCardReason ?? ''"
                              (valueChange)="alterarMotivo(atleta, $event)"
                            />
                          </td>
                        </tr>
                      }
                    }
                  </tbody>
                </table>
              </div>
            </app-card>
          }

          @if (etapa() === 3) {
            <app-card heading="Validação">
              @if (problemas().length === 0) {
                <app-alert tone="success">Placar, gols e goleiros estão coerentes.</app-alert>
              } @else {
                <app-alert tone="warning"
                  ><ul>
                    @for (problema of problemas(); track problema) {
                      <li>{{ problema }}</li>
                    }
                  </ul></app-alert
                >
              }
            </app-card>
          }

          @if (etapa() === 4) {
            <app-card heading="Revisão">
              <p class="resumo">
                {{ sumula()!.homeTeamName }} <strong>{{ mandante() }} × {{ visitante() }}</strong>
                {{ sumula()!.awayTeamName }}
              </p>
              <p>{{ participantes().length }} atleta(s) marcado(s) como participante(s).</p>
              <app-button
                escrita
                [loading]="salvando()"
                [disabled]="problemas().length > 0"
                (pressed)="salvar()"
                >Salvar súmula</app-button
              >
            </app-card>
          }

          <div class="acoes">
            <app-button variant="secondary" [disabled]="etapa() === 0" (pressed)="anterior()"
              >Voltar</app-button
            >
            @if (etapa() < 4) {
              <app-button (pressed)="proxima()">Continuar</app-button>
            }
          </div>
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './match-sheet.scss'],
})
export class MatchSheetPage {
  private readonly service = inject(MatchSheetService);
  private readonly route = inject(ActivatedRoute);
  private readonly contexto = inject(CompetitionContext);

  protected readonly nomesDasEtapas = ['Placar', 'Participação', 'Eventos', 'Validação', 'Revisão'];
  protected readonly camposDeEvento: readonly CampoNumerico[] = [
    'goals',
    'assists',
    'goalkeeperSaves',
    'penaltySaves',
    'yellowCards',
    'redCards',
    'ownGoals',
    'penaltyMisses',
  ];
  protected readonly motivos: readonly SelectOption[] = [
    { value: 'SecondYellow', label: 'Segundo amarelo' },
    { value: 'Direct', label: 'Vermelho direto' },
  ];

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly sumula = signal<MatchSheet | null>(null);
  protected readonly atletas = signal<readonly MatchSheetAthlete[]>([]);
  protected readonly mandante = signal(0);
  protected readonly visitante = signal(0);
  protected readonly etapa = signal(0);
  protected readonly salvando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly titulo = computed(() => {
    const atual = this.sumula();
    return atual ? `${atual.homeTeamName} × ${atual.awayTeamName}` : 'Partida';
  });
  protected readonly participantes = computed(() => this.atletas().filter((item) => item.didPlay));
  protected readonly times = computed(() => {
    const atual = this.sumula();
    return atual
      ? [
          { id: atual.homeTeamId, nome: atual.homeTeamName },
          { id: atual.awayTeamId, nome: atual.awayTeamName },
        ]
      : [];
  });
  protected readonly problemas = computed(() => this.validar());

  constructor() {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected atletasDoTime(teamId: string): readonly MatchSheetAthlete[] {
    return this.atletas().filter((item) => item.realTeamId === teamId);
  }

  protected posicao(position: string): string {
    return (
      { Goalkeeper: 'Goleiro', Defender: 'Defensor', Midfielder: 'Meia', Forward: 'Atacante' }[
        position
      ] ?? position
    );
  }

  protected maximo(campo: CampoNumerico): number {
    if (campo === 'yellowCards') return 2;
    if (campo === 'redCards') return 1;
    return campo === 'goalkeeperSaves' ? 999 : 99;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    const competitionId = this.contexto.campeonato()?.id ?? '';
    const matchId = this.route.snapshot.paramMap.get('partida') ?? '';
    this.service.get(competitionId, matchId).subscribe({
      next: (sheet) => {
        this.sumula.set(sheet);
        this.atletas.set(sheet.athletes);
        this.mandante.set(sheet.homeScore);
        this.visitante.set(sheet.awayScore);
        this.estado.set({ tipo: 'pronto' });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected alterarPlacar(home: boolean, event: Event): void {
    const value = this.numero(event, 99);
    (home ? this.mandante : this.visitante).set(value);
  }

  protected alternar(
    atleta: MatchSheetAthlete,
    campo: 'didPlay' | 'playedAsGoalkeeper',
    event: Event,
  ): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (campo === 'didPlay' && !checked) {
      this.substituir(atleta, {
        didPlay: false,
        playedAsGoalkeeper: false,
        goalsConceded: 0,
        goals: 0,
        assists: 0,
        goalkeeperSaves: 0,
        penaltySaves: 0,
        yellowCards: 0,
        redCards: 0,
        redCardReason: null,
        ownGoals: 0,
        penaltyMisses: 0,
      });
      return;
    }

    this.substituir(atleta, { [campo]: checked });
  }

  protected alterarNumero(atleta: MatchSheetAthlete, campo: CampoNumerico, event: Event): void {
    const value = this.numero(event, this.maximo(campo));
    const extra = campo === 'redCards' && value === 0 ? { redCardReason: null } : {};
    this.substituir(atleta, { [campo]: value, ...extra });
  }

  protected alterarMotivo(atleta: MatchSheetAthlete, value: string): void {
    this.substituir(atleta, { redCardReason: value as RedCardReason });
  }

  protected anterior(): void {
    this.etapa.update((value) => Math.max(0, value - 1));
  }

  protected proxima(): void {
    this.etapa.update((value) => Math.min(4, value + 1));
  }

  protected salvar(): void {
    const sheet = this.sumula();
    if (!sheet || this.salvando() || this.problemas().length > 0) return;

    this.salvando.set(true);
    this.retorno.set(null);
    this.service
      .save(this.contexto.campeonato()?.id ?? '', sheet.matchId, {
        homeScore: this.mandante(),
        awayScore: this.visitante(),
        appearances: this.atletas(),
        version: sheet.version,
      })
      .subscribe({
        next: (saved) => {
          this.sumula.set(saved);
          this.atletas.set(saved.athletes);
          this.mandante.set(saved.homeScore);
          this.visitante.set(saved.awayScore);
          this.salvando.set(false);
          this.retorno.set({ tom: 'success', texto: 'Súmula salva para revisão.' });
        },
        error: (falha: ApiFailure) => {
          this.salvando.set(false);
          this.retorno.set({
            tom: falha.status === 409 ? 'warning' : 'danger',
            texto: primeiroErro(falha) ?? falha.message,
          });
          if (falha.status === 409) this.carregar();
        },
      });
  }

  private validar(): string[] {
    const sheet = this.sumula();
    if (!sheet) return [];
    const issues: string[] = [];
    this.validarTime(
      sheet.homeTeamId,
      sheet.homeTeamName,
      this.mandante(),
      this.visitante(),
      issues,
    );
    this.validarTime(
      sheet.awayTeamId,
      sheet.awayTeamName,
      this.visitante(),
      this.mandante(),
      issues,
    );
    for (const athlete of this.participantes()) {
      if (athlete.redCards > 0 && !athlete.redCardReason) {
        issues.push(`Informe o motivo da expulsão de ${athlete.sportingName}.`);
      }
      if (athlete.redCardReason === 'SecondYellow' && athlete.yellowCards !== 2) {
        issues.push(`${athlete.sportingName}: segundo amarelo exige dois cartões amarelos.`);
      }
    }
    return issues;
  }

  private validarTime(
    teamId: string,
    teamName: string,
    score: number,
    opponentScore: number,
    issues: string[],
  ): void {
    const team = this.participantes().filter((item) => item.realTeamId === teamId);
    const opponent = this.participantes().filter((item) => item.realTeamId !== teamId);
    if (
      team.reduce((total, item) => total + item.goals, 0) +
        opponent.reduce((total, item) => total + item.ownGoals, 0) !==
      score
    ) {
      issues.push(`Os gols informados não explicam o placar de ${teamName}.`);
    }
    const goalkeepers = team.filter((item) => item.playedAsGoalkeeper);
    if (goalkeepers.length === 0) {
      issues.push(`Marque ao menos um goleiro de ${teamName}.`);
    } else if (
      goalkeepers.length > 1 &&
      goalkeepers.reduce((total, item) => total + item.goalsConceded, 0) !== opponentScore
    ) {
      issues.push(
        `Distribua os ${opponentScore} gol(s) sofridos entre os goleiros de ${teamName}.`,
      );
    }
  }

  private substituir(athlete: MatchSheetAthlete, changes: Partial<MatchSheetAthlete>): void {
    this.atletas.update((items) =>
      items.map((item) => (item.athleteId === athlete.athleteId ? { ...item, ...changes } : item)),
    );
  }

  private numero(event: Event, maximum: number): number {
    const parsed = Number((event.target as HTMLInputElement).value);
    return Number.isFinite(parsed) ? Math.min(maximum, Math.max(0, Math.trunc(parsed))) : 0;
  }
}

function primeiroErro(falha: ApiFailure): string | null {
  const errors = falha.problem?.['errors'];
  if (typeof errors !== 'object' || errors === null) return null;
  for (const messages of Object.values(errors as Record<string, unknown>)) {
    if (Array.isArray(messages) && typeof messages[0] === 'string') return messages[0];
  }
  return null;
}
