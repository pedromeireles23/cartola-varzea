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
import { Alert, Badge, Button, Card, Loading } from '../../shared/ui';
import {
  AthleteTotals,
  POSITION_LABELS,
  PublicAthlete,
  PublicCatalogService,
} from './public-catalog.service';
import { PublicNav } from './public-nav';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly atleta: PublicAthlete }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Perfil público do atleta (02 §9.1, `/c/:campeonato/atletas/:atletaId`).
 *
 * O 01 §11 define o limite: nome esportivo, posição, time, preço e o que ele fez em
 * campo. Nada de contato, documento ou data de nascimento — que nem chegam a sair do
 * servidor. As estatísticas contam só rodada publicada, pela mesma razão do calendário.
 */
@Component({
  selector: 'app-public-athlete',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading, PublicNav, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato()]">← Página do campeonato</a></p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Atleta</h1>
        <app-card><app-loading label="Abrindo o perfil…" /></app-card>
      }
      @case ('erro') {
        <h1>Atleta</h1>
        <app-card>
          <app-alert tone="warning">
            Não encontramos este atleta. Ele pode ter saído do campeonato ou o endereço estar
            errado.
          </app-alert>
          <a class="acao" [routerLink]="['/c', campeonato()]">Ver o campeonato</a>
          <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ atleta()!.sportingName }}</h1>
        <app-public-nav [campeonato]="campeonato()" atual="visao" />
        <p class="apoio">
          {{ posicao() }} ·
          <a [routerLink]="['/c', campeonato(), 'times', atleta()!.realTeamId]">
            {{ atleta()!.realTeamName }}
          </a>
          · {{ creditos(atleta()!.price) }}
          @if (!atleta()!.active) {
            <app-badge>fora do elenco</app-badge>
          }
        </p>

        <app-card heading="No campeonato">
          @if (atleta()!.totals.matches === 0) {
            <app-alert tone="info">
              Este atleta ainda não entrou em campo numa rodada com resultado publicado.
            </app-alert>
          } @else {
            <ul class="eventos">
              @for (item of feitos(atleta()!.totals); track item.nome) {
                <li class="evento">
                  <span>{{ item.nome }}</span>
                  <strong class="evento__valor">{{ item.valor }}</strong>
                </li>
              }
            </ul>
          }
        </app-card>

        <app-card heading="Como o preço andou">
          @if (atleta()!.priceHistory.length === 0) {
            <p class="apoio">
              O preço muda quando a primeira rodada for apurada. Até lá, vale o preço inicial.
            </p>
          } @else {
            <ul class="eventos">
              @for (linha of atleta()!.priceHistory; track linha.sequence) {
                <li class="evento">
                  <span>{{ linha.roundName }}</span>
                  <strong
                    class="evento__valor"
                    [class.evento__valor--alta]="linha.variation > 0"
                    [class.evento__valor--queda]="linha.variation < 0"
                  >
                    @if (linha.variation === 0) {
                      preço mantido em {{ creditos(linha.newPrice) }}
                    } @else {
                      {{ creditos(linha.previousPrice) }} → {{ creditos(linha.newPrice) }} ({{
                        variacao(linha.variation)
                      }})
                    }
                  </strong>
                </li>
              }
            </ul>
          }
        </app-card>
      }
    }
  `,
  styleUrls: ['./public.scss', './fixtures.scss', './scoring-rules.scss'],
})
export class PublicAthletePage implements OnInit {
  private readonly service = inject(PublicCatalogService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Identificador do atleta na rota. */
  readonly atletaId = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly atleta = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.atleta : null;
  });

  protected readonly posicao = computed(() => {
    const codigo = this.atleta()?.position ?? '';
    return POSITION_LABELS[codigo] ?? codigo;
  });

  ngOnInit(): void {
    this.carregar();
  }

  /**
   * Os números que aconteceram. Jogos sempre aparece, porque é o denominador de tudo;
   * o resto entra só quando houve, para o perfil não virar uma coluna de zeros.
   */
  protected feitos(totais: AthleteTotals): readonly { nome: string; valor: number }[] {
    const linhas: { nome: string; valor: number }[] = [{ nome: 'Jogos', valor: totais.matches }];
    const talvez = (nome: string, valor: number) => {
      if (valor > 0) {
        linhas.push({ nome, valor });
      }
    };

    talvez('Gols', totais.goals);
    talvez('Assistências', totais.assists);
    talvez('Defesas', totais.goalkeeperSaves);
    talvez('Pênaltis defendidos', totais.penaltySaves);
    talvez('Jogos sem sofrer gol', totais.cleanSheets);
    talvez('Gols contra', totais.ownGoals);
    talvez('Pênaltis perdidos', totais.penaltyMisses);
    talvez('Cartões amarelos', totais.yellowCards);
    talvez('Cartões vermelhos', totais.redCards);
    return linhas;
  }

  protected creditos(valor: number): string {
    return `C$ ${this.numero(valor)}`;
  }

  protected variacao(valor: number): string {
    return `${valor > 0 ? '+' : ''}${this.numero(valor)}`;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.athlete(this.campeonato(), this.atletaId()).subscribe({
      next: (atleta) => {
        this.estado.set({ tipo: 'pronto', atleta });
        this.meta.set({
          title: atleta.sportingName,
          description: `${atleta.sportingName}, ${POSITION_LABELS[atleta.position] ?? atleta.position} do ${atleta.realTeamName} em ${atleta.competitionName}: estatísticas e histórico de preço.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  private numero(valor: number): string {
    return valor.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
}
