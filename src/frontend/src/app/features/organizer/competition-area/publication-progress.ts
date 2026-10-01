import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Check, CircleAlert, CircleDashed, type IconNode } from 'lucide';

import { Icon } from '../../../shared/ui';
import { CompetitionReadiness, ReadinessItem } from './publication.service';

type EstadoEtapa = 'pronta' | 'alerta' | 'pendente';

interface Etapa {
  readonly rotulo: string;
  /** Onde o organizador resolve a etapa, na navegação da área. */
  readonly rota: string;
  /** Códigos do checklist do servidor (CompetitionReadiness) que pertencem à etapa. */
  readonly codigos: readonly string[];
}

/**
 * As etapas são as regras do checklist do servidor, agrupadas como o organizador pensa
 * o campeonato. O mapa cobre os sete códigos de `CompetitionReadiness`; um código novo
 * que ninguém agrupou continua aparecendo na lista do checklist, só não ganha etapa.
 */
const ETAPAS: readonly Etapa[] = [
  { rotulo: 'Fases', rota: 'fases', codigos: ['no_stages'] },
  {
    rotulo: 'Times nas fases',
    rota: 'fases',
    codigos: ['no_stage_participants', 'stage_without_participants'],
  },
  { rotulo: 'Times', rota: 'times', codigos: ['not_enough_teams'] },
  { rotulo: 'Atletas', rota: 'atletas', codigos: ['not_enough_athletes', 'thin_real_team_roster'] },
  { rotulo: 'Preços', rota: 'atletas', codigos: ['single_price_level'] },
];

const ICONES: Readonly<Record<EstadoEtapa, IconNode>> = {
  pronta: Check,
  alerta: CircleAlert,
  pendente: CircleDashed,
};

const TEXTOS: Readonly<Record<EstadoEtapa, string>> = {
  pronta: 'pronta',
  alerta: 'com alerta',
  pendente: 'pendente',
};

function estadoDa(etapa: Etapa, itens: readonly ReadinessItem[]): EstadoEtapa {
  const daEtapa = itens.filter((item) => etapa.codigos.includes(item.code));
  if (daEtapa.some((item) => item.severity === 'Blocker')) {
    return 'pendente';
  }
  return daEtapa.length > 0 ? 'alerta' : 'pronta';
}

/**
 * Progresso da publicação na Central da competição (V6): as etapas do checklist, cada
 * uma dita por ícone e por texto — nunca só pela cor —, e a publicação como última
 * etapa. Alerta não segura a publicação, então conta como etapa cumprida.
 */
@Component({
  selector: 'app-publication-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink],
  template: `
    <div class="progresso">
      <p class="progresso__resumo">
        <strong class="progresso__numero num">{{ cumpridas() }} de {{ total() }}</strong>
        etapas cumpridas para o campeonato ir ao ar
      </p>
      <div class="progresso__barra" aria-hidden="true">
        <span class="progresso__preenchido" [style.width.%]="percentual()"></span>
      </div>

      <ol class="etapas">
        @for (etapa of etapas(); track etapa.rotulo) {
          <li class="etapa" [class]="'etapa--' + etapa.estado">
            <svg class="etapa__icone" [appIcon]="icone(etapa.estado)" [size]="18" />
            <span class="etapa__rotulo">{{ etapa.rotulo }}</span>
            <!--
              O espaço vai dentro da interpolação: entre tags o Angular o remove, e o leitor
              de tela ouviria "Fasespronta".
            -->
            <span class="etapa__estado">{{ ' ' + texto(etapa.estado) }}</span>
            @if (etapa.estado !== 'pronta') {
              <a
                class="etapa__link"
                [routerLink]="['/organizar/c', prontidao().competitionId, etapa.rota]"
              >
                Resolver<span class="sr-only"> {{ etapa.rotulo }}</span>
              </a>
            }
          </li>
        }
        <li class="etapa" [class]="'etapa--' + (publicado() ? 'pronta' : 'pendente')">
          <svg
            class="etapa__icone"
            [appIcon]="icone(publicado() ? 'pronta' : 'pendente')"
            [size]="18"
          />
          <span class="etapa__rotulo">Publicação</span>
          <span class="etapa__estado">
            {{
              publicado() ? 'no ar' : prontidao().canPublish ? 'pronta para publicar' : 'pendente'
            }}
          </span>
        </li>
      </ol>
    </div>
  `,
  styleUrl: './publication-progress.scss',
})
export class PublicationProgress {
  readonly prontidao = input.required<CompetitionReadiness>();

  protected readonly publicado = computed(() => this.prontidao().status === 'Published');

  protected readonly etapas = computed(() =>
    ETAPAS.map((etapa) => ({ ...etapa, estado: estadoDa(etapa, this.prontidao().items) })),
  );

  /** As etapas do checklist mais a publicação. */
  protected readonly total = computed(() => ETAPAS.length + 1);

  protected readonly cumpridas = computed(
    () =>
      this.etapas().filter((etapa) => etapa.estado !== 'pendente').length +
      (this.publicado() ? 1 : 0),
  );

  protected readonly percentual = computed(() => (this.cumpridas() / this.total()) * 100);

  protected icone(estado: EstadoEtapa): IconNode {
    return ICONES[estado];
  }

  protected texto(estado: EstadoEtapa): string {
    return TEXTOS[estado];
  }
}
