import { Injectable, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../../../core/api/problem-details';
import { Athlete, AthleteService } from '../athlete.service';
import { Coach, CoachService } from '../coach.service';
import { CompetitionContext } from '../competition-context';
import { CompetitionReadiness, PublicationService } from '../publication.service';
import { RealTeam, RealTeamService } from '../real-team.service';
import { Round, RoundService } from '../round.service';
import { Stage, StageService } from '../stage.service';

/** Os passos da montagem, na ordem em que o campeonato depende de cada um. */
export type SetupStep = 'times' | 'elencos' | 'formato' | 'tabela' | 'publicar';

export const SETUP_STEPS: readonly SetupStep[] = [
  'times',
  'elencos',
  'formato',
  'tabela',
  'publicar',
];

export const SETUP_STEP_LABELS: Readonly<Record<SetupStep, string>> = {
  times: 'Times',
  elencos: 'Elencos',
  formato: 'Formato',
  tabela: 'Tabela',
  publicar: 'Publicar',
};

export interface SetupData {
  /** Ativos e arquivados, na ordem do servidor. */
  readonly teams: readonly RealTeam[];
  readonly athletes: readonly Athlete[];
  readonly coaches: readonly Coach[];
  readonly stages: readonly Stage[];
  readonly rounds: readonly Round[];
  readonly readiness: CompetitionReadiness;
}

export interface SetupStepView {
  readonly step: SetupStep;
  readonly label: string;
  readonly done: boolean;
  /** Uma frase curta do que já existe, para o passo dizer onde a pessoa está. */
  readonly detail: string;
}

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly dados: SetupData }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * O que a montagem do campeonato precisa saber, lido uma vez e compartilhado pelos
 * passos. Cada passo grava pelo serviço de sempre e pede `reload()`: o estado de cada
 * passo sai dos dados do campeonato, não de um rascunho guardado à parte, então dá para
 * sair no meio e voltar outro dia.
 */
@Injectable()
export class SetupStore {
  private readonly contexto = inject(CompetitionContext);
  private readonly teamsApi = inject(RealTeamService);
  private readonly athletesApi = inject(AthleteService);
  private readonly coachesApi = inject(CoachService);
  private readonly stagesApi = inject(StageService);
  private readonly roundsApi = inject(RoundService);
  private readonly publication = inject(PublicationService);

  readonly estado = signal<Estado>({ tipo: 'carregando' });

  readonly dados = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.dados : null;
  });

  readonly falha = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  });

  readonly campeonatoId = computed(() => this.contexto.campeonato()?.id ?? '');

  /** Atletas inscritos que a modalidade pede em cada time. */
  readonly minimo = computed(
    () => this.contexto.campeonato()?.modalityProfile.minimumAthletesPerRealTeam ?? 0,
  );

  readonly timesAtivos = computed(() => (this.dados()?.teams ?? []).filter((t) => !t.isArchived));

  readonly atletasAtivos = computed(() =>
    (this.dados()?.athletes ?? []).filter((athlete) => athlete.status === 'Active'),
  );

  /** Times com menos atletas inscritos do que a modalidade pede. */
  readonly timesIncompletos = computed(() =>
    this.timesAtivos().filter((team) => this.elencoDe(team.id).length < this.minimo()),
  );

  readonly jogos = computed(() => (this.dados()?.rounds ?? []).flatMap((round) => round.matches));

  readonly passos = computed<readonly SetupStepView[]>(() => {
    const dados = this.dados();
    if (!dados) return [];

    const times = this.timesAtivos().length;
    const completos = times - this.timesIncompletos().length;
    const faltaPosicao = dados.readiness.items.some((item) => item.code === 'not_enough_athletes');
    const comTimes = dados.stages.filter((stage) => stage.participants.length > 0);
    const jogos = this.jogos();
    const rodadas = dados.rounds.filter((round) => round.matches.length > 0).length;
    const semJogo = comTimes.filter((stage) => !jogos.some((jogo) => jogo.stageId === stage.id));
    const publicado = dados.readiness.status === 'Published';

    return [
      {
        step: 'times',
        label: SETUP_STEP_LABELS.times,
        done: times >= 2,
        detail: times === 1 ? '1 time' : `${times} times`,
      },
      {
        step: 'elencos',
        label: SETUP_STEP_LABELS.elencos,
        done: times >= 2 && completos === times && !faltaPosicao,
        detail: times === 0 ? 'sem times' : `${completos} de ${times} completos`,
      },
      {
        step: 'formato',
        label: SETUP_STEP_LABELS.formato,
        done: comTimes.length > 0,
        detail:
          dados.stages.length === 0
            ? 'a definir'
            : dados.stages.length === 1
              ? dados.stages[0].name
              : `${dados.stages.length} fases`,
      },
      {
        step: 'tabela',
        label: SETUP_STEP_LABELS.tabela,
        done: comTimes.length > 0 && semJogo.length === 0,
        detail:
          jogos.length === 0
            ? 'sem jogos'
            : `${jogos.length} ${jogos.length === 1 ? 'jogo' : 'jogos'} em ${rodadas} ${rodadas === 1 ? 'rodada' : 'rodadas'}`,
      },
      {
        step: 'publicar',
        label: SETUP_STEP_LABELS.publicar,
        done: publicado,
        detail: publicado ? 'no ar' : 'rascunho',
      },
    ];
  });

  /** O primeiro passo por fazer; com tudo pronto, a publicação. */
  readonly primeiroPendente = computed<SetupStep>(
    () => this.passos().find((passo) => !passo.done)?.step ?? 'publicar',
  );

  elencoDe(teamId: string): readonly Athlete[] {
    return this.atletasAtivos().filter((athlete) => athlete.realTeamId === teamId);
  }

  tecnicoDe(teamId: string): Coach | null {
    return this.dados()?.coaches.find((coach) => coach.realTeamId === teamId) ?? null;
  }

  load(): void {
    this.estado.set({ tipo: 'carregando' });
    this.buscar();
  }

  /** Busca de novo sem esconder a tela, depois de uma gravação. */
  reload(): void {
    this.buscar();
  }

  private buscar(): void {
    const id = this.campeonatoId();
    forkJoin({
      teams: this.teamsApi.list(id),
      athletes: this.athletesApi.list(id),
      coaches: this.coachesApi.list(id),
      stages: this.stagesApi.list(id),
      rounds: this.roundsApi.list(id),
      readiness: this.publication.readiness(id),
    }).subscribe({
      next: (dados) => this.estado.set({ tipo: 'pronto', dados }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
