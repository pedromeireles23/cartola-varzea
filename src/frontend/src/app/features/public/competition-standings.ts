import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { PageMetaService } from '../../core/seo/page-meta';
import { Alert, BackLink, Button, Card, Loading } from '../../shared/ui';
import { PublicFixtureService, PublicStandings, StageStandings } from './public-fixture.service';
import { PublicNav } from './public-nav';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly tabela: PublicStandings }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Os critérios de desempate em pt-BR, na ordem que a organização escolheu. */
const CRITERIOS: Readonly<Record<string, string>> = {
  Wins: 'mais vitórias',
  GoalDifference: 'saldo de gols',
  GoalsFor: 'gols marcados',
  HeadToHead: 'confronto direto',
  FewestRedCards: 'menos cartões vermelhos',
  FewestYellowCards: 'menos cartões amarelos',
};

/**
 * Classificação por fase e grupo (02 §9.1, `/c/:campeonato/tabela`).
 *
 * Mata-mata não tem tabela — o organizador escolhe o formato conforme a quantidade de
 * times, e um campeonato pequeno pode ser só eliminatória. A fase vem declarada assim
 * pelo servidor, e a tela diz isso em vez de mostrar um vazio sem explicação.
 *
 * A ordem dos critérios de desempate aparece escrita, porque uma tabela que não conta
 * por que um time está na frente do outro vira motivo de discussão no grupo.
 */
@Component({
  selector: 'app-competition-standings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Button, Card, Loading, PublicNav, RouterLink],
  template: `
    <app-back-link [link]="['/c', campeonato()]" label="Página do campeonato" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Tabela</h1>
        <app-card><app-loading label="Montando a classificação…" /></app-card>
      }
      @case ('erro') {
        <h1>Tabela</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>Tabela de {{ tabela()!.name }}</h1>
        <app-public-nav [campeonato]="campeonato()" atual="tabela" />
        <p class="apoio">
          Só entra resultado já publicado. Partida adiada ou cancelada fica de fora da conta.
        </p>

        @for (fase of fases(); track fase.sequence) {
          <app-card [heading]="fase.sequence + '. ' + fase.name">
            @if (fase.format !== 'Groups') {
              <app-alert tone="info">
                Esta fase é de mata-mata: quem perde está fora, então ela não tem classificação por
                pontos.
              </app-alert>
              <a class="acao acao--secundaria" [routerLink]="['/c', campeonato(), 'partidas']">
                Ver os confrontos
              </a>
            } @else {
              <p class="apoio">{{ desempate(fase) }}</p>

              @for (grupo of fase.groups; track $index) {
                <section class="grupo-tabela">
                  @if (grupo.name) {
                    <h3 class="grupo-tabela__nome">{{ grupo.name }}</h3>
                  }
                  <table class="tabela">
                    <caption class="sr-only">
                      Classificação
                      {{
                        grupo.name ? 'do ' + grupo.name : 'da ' + fase.name
                      }}
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col" class="tabela__posicao">#</th>
                        <th scope="col">Time</th>
                        <th scope="col" title="Pontos">P</th>
                        <th scope="col" title="Jogos">J</th>
                        <th scope="col" title="Vitórias">V</th>
                        <th scope="col" title="Empates">E</th>
                        <th scope="col" title="Derrotas">D</th>
                        <th scope="col" title="Saldo de gols">SG</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (linha of grupo.rows; track linha.teamId) {
                        <tr>
                          <td class="tabela__posicao">
                            {{ linha.position }}º
                            @if (linha.tied) {
                              <span class="sr-only">, empatado</span>
                            }
                          </td>
                          <th scope="row" class="tabela__time">
                            <a [routerLink]="['/c', campeonato(), 'times', linha.teamId]">
                              {{ linha.teamName }}
                            </a>
                            @if (linha.tied) {
                              <span class="tabela__empate">empatado</span>
                            }
                          </th>
                          <td class="tabela__pontos">{{ linha.points }}</td>
                          <td>{{ linha.played }}</td>
                          <td>{{ linha.wins }}</td>
                          <td>{{ linha.draws }}</td>
                          <td>{{ linha.losses }}</td>
                          <td>{{ saldo(linha.goalDifference) }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </section>
              } @empty {
                <p class="apoio">
                  Nenhum time confirmado nesta fase ainda. A tabela aparece quando a liga definir
                  quem joga.
                </p>
              }
            }
          </app-card>
        } @empty {
          <app-card>
            <app-alert tone="info">
              Este campeonato ainda não tem fases publicadas. A tabela aparece quando a liga definir
              o formato e os times.
            </app-alert>
          </app-card>
        }
      }
    }
  `,
  styleUrls: ['./public.scss', './standings.scss'],
})
export class CompetitionStandingsPage implements OnInit {
  private readonly service = inject(PublicFixtureService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly tabela = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.tabela : null;
  });

  protected readonly fases = computed(() => this.tabela()?.stages ?? []);

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  /**
   * A ordem dos critérios, escrita. Sem critério nenhum a tabela pode terminar com
   * empate, e é mais honesto dizer isso do que sugerir uma ordem que não existe.
   */
  protected desempate(fase: StageStandings): string {
    if (fase.tiebreakers.length === 0) {
      return 'Empate em pontos não é desfeito nesta fase: os times dividem a colocação.';
    }

    const criterios = fase.tiebreakers.map((item) => CRITERIOS[item] ?? item).join(', depois ');
    return `Empate em pontos é decidido por ${criterios}.`;
  }

  /** O saldo vem com sinal: "+3" diz mais que "3" numa coluna que aceita negativo. */
  protected saldo(valor: number): string {
    return valor > 0 ? `+${valor}` : `${valor}`;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.standings(this.campeonato()).subscribe({
      next: (tabela) => {
        this.estado.set({ tipo: 'pronto', tabela });
        this.meta.set({
          title: `Tabela de ${tabela.name}`,
          description: `Classificação de ${tabela.name} por fase e grupo, com pontos, jogos, vitórias, empates, derrotas e saldo de gols.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
