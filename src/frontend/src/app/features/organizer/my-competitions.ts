import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Badge, Card, FailureState, Loading } from '../../shared/ui';
import {
  CompetitionService,
  MODALITY_LABELS,
  MyCompetition,
  STATUS_LABELS,
} from './competition.service';
import { teamInitials } from './competition-area/setup/setup-text';
import { MyOrganization, OrganizationService } from './organization.service';
import { NextStep, nextStep } from './organizer-next-step';

type Estado =
  | { readonly tipo: 'carregando' }
  | {
      readonly tipo: 'pronto';
      readonly organizacoes: readonly MyOrganization[];
      readonly campeonatos: readonly MyCompetition[];
    }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

interface Cartao {
  readonly campeonato: MyCompetition;
  readonly passo: NextStep;
}

interface Grupo {
  readonly organizacao: MyOrganization;
  readonly cartoes: readonly Cartao[];
}

/**
 * Meus campeonatos (`/organizar`): a porta da área de quem organiza.
 *
 * O campeonato é o que a pessoa procura, então ele vem primeiro, com o próximo passo e o
 * atalho para dar esse passo; a organização vira o agrupamento, com criar campeonato e a
 * equipe no cabeçalho dela. Antes, chegar a um campeonato pedia passar pela lista de
 * organizações e pela de campeonatos da organização.
 */
@Component({
  selector: 'app-my-competitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'theme-organizer' },
  imports: [Badge, Card, FailureState, Loading, RouterLink],
  template: `
    <h1>Meus campeonatos</h1>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Buscando seus campeonatos…" skeleton="cards" />
        </app-card>
      }

      @case ('erro') {
        <app-card>
          <app-failure-state [failure]="falha()!" (retry)="carregar()" />
        </app-card>
      }

      @case ('pronto') {
        @for (grupo of grupos(); track grupo.organizacao.id) {
          <section class="org" [attr.aria-labelledby]="'org-' + grupo.organizacao.id">
            <div class="org__topo">
              <h2 class="org__nome" [id]="'org-' + grupo.organizacao.id">
                {{ grupo.organizacao.name }}
              </h2>
              @if (grupo.organizacao.role === 'Owner') {
                <app-badge tone="brand">Proprietário</app-badge>
              } @else {
                <app-badge>Auxiliar</app-badge>
              }
              @if (grupo.organizacao.role === 'Owner') {
                <div class="org__acoes">
                  <a
                    class="acao acao--principal"
                    [routerLink]="['/organizar/o', grupo.organizacao.id, 'campeonatos', 'novo']"
                    >Criar campeonato</a
                  >
                  <a class="acao" [routerLink]="['/organizar/o', grupo.organizacao.id, 'equipe']">
                    Gerenciar equipe
                  </a>
                </div>
              }
            </div>

            <ul class="campeonatos">
              @for (cartao of grupo.cartoes; track cartao.campeonato.id) {
                <li class="campeonato" [class.campeonato--urgente]="cartao.passo.urgente">
                  <div class="campeonato__topo">
                    <span class="escudo" aria-hidden="true">{{
                      iniciais(cartao.campeonato.name)
                    }}</span>
                    <h3 class="campeonato__nome">
                      <a [routerLink]="['/organizar/c', cartao.campeonato.id]">{{
                        cartao.campeonato.name
                      }}</a>
                    </h3>
                    <app-badge
                      [tone]="cartao.campeonato.status === 'Draft' ? 'warning' : 'success'"
                    >
                      {{ status(cartao.campeonato) }}
                    </app-badge>
                  </div>
                  <p class="campeonato__detalhe">{{ detalhe(cartao.campeonato) }}</p>
                  <p class="campeonato__passo">{{ cartao.passo.texto }}</p>
                  <a
                    class="acao"
                    [class.acao--principal]="cartao.passo.urgente"
                    [routerLink]="cartao.passo.link"
                  >
                    {{ cartao.passo.rotulo
                    }}<span class="sr-only">{{ ' em ' + cartao.campeonato.name }}</span>
                  </a>
                </li>
              } @empty {
                <li class="campeonato campeonato--vazio">
                  <p class="apoio">
                    {{
                      grupo.organizacao.role === 'Owner'
                        ? 'Nenhum campeonato ainda. Crie o primeiro: ele nasce em rascunho, e só a organização vê até a publicação.'
                        : 'Nenhum campeonato ainda. Quem é proprietário da organização cria os campeonatos.'
                    }}
                  </p>
                </li>
              }
            </ul>
          </section>
        } @empty {
          <app-card heading="Nenhuma organização ainda">
            <p class="apoio">
              Para organizar campeonatos, peça acesso. Se recebeu um convite para auxiliar, abra o
              link que chegou por e-mail.
            </p>
            <a class="acao" routerLink="/organizar/solicitar">Solicitar acesso</a>
          </app-card>
        }

        @if (grupos().length > 0) {
          <p class="rodape">
            Organiza outra liga? <a routerLink="/organizar/solicitar">Peça acesso para ela</a>.
          </p>
        }
      }
    }
  `,
  styleUrls: ['./organizer.scss', './my-competitions.scss'],
})
export class MyCompetitionsPage {
  private readonly organizations = inject(OrganizationService);
  private readonly competitions = inject(CompetitionService);

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  /**
   * Uma seção por organização, na ordem do servidor. Dentro dela, primeiro o que pede
   * súmula, depois os publicados e por fim os rascunhos, cada parte do mais recente.
   */
  protected readonly grupos = computed<readonly Grupo[]>(() => {
    const atual = this.estado();
    if (atual.tipo !== 'pronto') return [];
    return atual.organizacoes.map((organizacao) => ({
      organizacao,
      cartoes: atual.campeonatos
        .filter((campeonato) => campeonato.organizationId === organizacao.id)
        .map((campeonato) => ({ campeonato, passo: nextStep(campeonato) }))
        .sort((a, b) => peso(a) - peso(b)),
    }));
  });

  constructor() {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected status(campeonato: MyCompetition): string {
    return STATUS_LABELS[campeonato.status];
  }

  protected detalhe(campeonato: MyCompetition): string {
    const times = campeonato.activeTeams;
    const jogos = campeonato.matches;
    return [
      MODALITY_LABELS[campeonato.modality],
      `temporada ${campeonato.season}`,
      `${times} ${times === 1 ? 'time' : 'times'}`,
      `${jogos} ${jogos === 1 ? 'jogo' : 'jogos'}`,
    ].join(' · ');
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    forkJoin({
      organizacoes: this.organizations.mine(),
      campeonatos: this.competitions.mine(),
    }).subscribe({
      next: ({ organizacoes, campeonatos }) =>
        this.estado.set({ tipo: 'pronto', organizacoes, campeonatos }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}

/** O que pede súmula vem antes; depois os publicados; o rascunho por último. */
function peso({ campeonato, passo }: Cartao): number {
  if (passo.urgente) return 0;
  return campeonato.status === 'Published' ? 1 : 2;
}
