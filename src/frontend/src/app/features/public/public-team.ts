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
import { POSITION_LABELS, PublicCatalogService, PublicTeamDetail } from './public-catalog.service';
import { PublicNav } from './public-nav';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly time: PublicTeamDetail }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Time e elenco (02 §9.1, `/c/:campeonato/times/:timeId`).
 *
 * De cada pessoa aparece o que o 01 §11 permite: nome esportivo, posição e preço. Quem
 * foi liberado no meio do campeonato continua listado, mas marcado — ele jogou, e
 * apagá-lo faria a história do time não fechar.
 */
@Component({
  selector: 'app-public-team',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading, PublicNav, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato()]">← Página do campeonato</a></p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Time</h1>
        <app-card><app-loading label="Abrindo o elenco…" /></app-card>
      }
      @case ('erro') {
        <h1>Time</h1>
        <app-card>
          <app-alert tone="warning">
            Não encontramos este time. Ele pode ter saído do campeonato ou o endereço estar errado.
          </app-alert>
          <a class="acao" [routerLink]="['/c', campeonato()]">Ver o campeonato</a>
          <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
        </app-card>
      }
      @case ('pronto') {
        <h1>{{ time()!.name }}</h1>
        <app-public-nav [campeonato]="campeonato()" atual="visao" />
        <p class="apoio">
          {{ time()!.competitionName }}
          @if (time()!.coachName; as tecnico) {
            · Técnico: {{ tecnico }}
          }
        </p>

        <app-card heading="Elenco">
          <ul class="elenco">
            @for (atleta of time()!.athletes; track atleta.id) {
              <li class="elenco__atleta" [class.elenco__atleta--fora]="!atleta.active">
                <span class="elenco__nome">
                  <a [routerLink]="['/c', campeonato(), 'atletas', atleta.id]">
                    {{ atleta.sportingName }}
                  </a>
                  @if (!atleta.active) {
                    <app-badge>fora do elenco</app-badge>
                  }
                </span>
                <span class="elenco__posicao">{{ posicao(atleta.position) }}</span>
                <span class="elenco__feitos">{{ creditos(atleta.price) }}</span>
              </li>
            } @empty {
              <li class="apoio">
                Nenhum atleta inscrito neste time ainda. Quando a liga cadastrar o elenco, ele
                aparece aqui.
              </li>
            }
          </ul>
        </app-card>
      }
    }
  `,
  styleUrls: ['./public.scss', './fixtures.scss'],
})
export class PublicTeamPage implements OnInit {
  private readonly service = inject(PublicCatalogService);
  private readonly meta = inject(PageMetaService);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Identificador do time na rota. */
  readonly timeId = input.required<string>();

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly time = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.time : null;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected posicao(codigo: string): string {
    return POSITION_LABELS[codigo] ?? codigo;
  }

  protected creditos(valor: number): string {
    return `C$ ${valor.toLocaleString('pt-BR', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}`;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.team(this.campeonato(), this.timeId()).subscribe({
      next: (time) => {
        this.estado.set({ tipo: 'pronto', time });
        this.meta.set({
          title: time.name,
          description: `Elenco de ${time.name} em ${time.competitionName}: atletas, posições e preços no fantasy.`,
          type: 'article',
        });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
