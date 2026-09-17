import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, FormField, Loading } from '../../shared/ui';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { PublicCompetitionService, PublicCompetitionSummary } from './public-competition.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonatos: readonly PublicCompetitionSummary[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Busca pública de campeonatos (02 §9.1, `/campeonatos`).
 *
 * Só existe o que foi publicado: o servidor nem conhece rascunho nesta rota. O termo
 * fica na URL (`?busca=`) para que o resultado possa ser compartilhado e sobreviva a
 * recarregar a página.
 */
@Component({
  selector: 'app-competition-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, DatePipe, FormField, Loading, RouterLink],
  template: `
    <h1>Campeonatos</h1>
    <p class="intro">
      Encontre um campeonato publicado por nome, temporada ou organização. Campeonatos em rascunho
      não aparecem aqui.
    </p>

    <app-card>
      <form class="busca" (submit)="buscar($event)" novalidate>
        <app-form-field
          label="Buscar campeonato"
          placeholder="Copa da Várzea"
          [maxLength]="buscaMax"
          [(value)]="termo"
        />
        <app-button type="submit" [loading]="estado().tipo === 'carregando'">Buscar</app-button>
      </form>
    </app-card>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Procurando campeonatos…" /></app-card>
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
        <ul class="resultados">
          @for (campeonato of campeonatos(); track campeonato.slug) {
            <li>
              <app-card>
                <article class="resultado">
                  <h2 class="resultado__nome">
                    <a [routerLink]="['/c', campeonato.slug]">{{ campeonato.name }}</a>
                  </h2>
                  <p class="resultado__detalhe">
                    {{ rotulo(campeonato.modality) }} · Temporada {{ campeonato.season }}
                  </p>
                  <p class="resultado__detalhe">
                    {{ campeonato.organizationName }} · publicado em
                    {{ campeonato.publishedAt | date: 'dd/MM/yyyy' }}
                  </p>
                </article>
              </app-card>
            </li>
          } @empty {
            <li>
              <app-card>
                <p class="apoio">
                  {{
                    termoAplicado()
                      ? 'Nenhum campeonato publicado corresponde a essa busca.'
                      : 'Nenhum campeonato publicado ainda. Volte em breve.'
                  }}
                </p>
              </app-card>
            </li>
          }
        </ul>
      }
    }
  `,
  styleUrl: './public.scss',
})
export class CompetitionSearchPage implements OnInit {
  private readonly service = inject(PublicCompetitionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Espelha o limite que o servidor aplica ao termo. */
  protected readonly buscaMax = 80;
  protected readonly termo = signal('');
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  /** Termo da última busca concluída; separa "nada publicado" de "nada encontrado". */
  protected readonly termoAplicado = signal('');

  protected readonly campeonatos = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonatos : [];
  });

  ngOnInit(): void {
    this.termo.set(this.route.snapshot.queryParamMap.get('busca') ?? '');
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected buscar(event: Event): void {
    event.preventDefault();

    // A URL é a fonte do termo: navegar primeiro deixa o resultado compartilhável.
    const busca = this.termo().trim();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { busca: busca || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.carregar();
  }

  protected carregar(): void {
    const busca = this.termo().trim();
    this.estado.set({ tipo: 'carregando' });
    this.service.search(busca).subscribe({
      next: (campeonatos) => {
        this.termoAplicado.set(busca);
        this.estado.set({ tipo: 'pronto', campeonatos });
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected rotulo(modality: PublicCompetitionSummary['modality']): string {
    return MODALITY_LABELS[modality];
  }
}
