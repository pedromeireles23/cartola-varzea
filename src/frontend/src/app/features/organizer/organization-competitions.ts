import { DatePipe } from '@angular/common';
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
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, BackLink, Badge, Button, Card, Loading } from '../../shared/ui';
import {
  CompetitionService,
  CompetitionSummary,
  MODALITY_LABELS,
  STATUS_LABELS,
} from './competition.service';
import { MyOrganization, OrganizationService } from './organization.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | {
      readonly tipo: 'pronto';
      readonly organizacao: MyOrganization;
      readonly campeonatos: readonly CompetitionSummary[];
    }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Campeonatos de uma organização (02 §9.1, `/organizar/o/:organizacao/campeonatos`).
 *
 * Proprietário e auxiliar veem a lista, inclusive rascunhos; só o proprietário
 * recebe o caminho para criar. A API decide pelos dois lados.
 */
@Component({
  selector: 'app-organization-competitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, BackLink, Badge, Button, Card, DatePipe, Loading, RouterLink],
  template: `
    <app-back-link link="/organizar" label="Minhas organizações" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Campeonatos</h1>
        <app-card>
          <app-loading label="Buscando os campeonatos…" />
        </app-card>
      }

      @case ('erro') {
        <h1>Campeonatos</h1>
        <app-card>
          @if (falha()!.status === 403 || falha()!.status === 404) {
            <app-alert tone="warning">
              Só quem faz parte da organização vê os campeonatos dela.
            </app-alert>
          } @else {
            <app-alert tone="danger">
              <p>{{ falha()!.message }}</p>
              @if (falha()!.traceId) {
                <p class="trace">Código de rastreio: {{ falha()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }

      @case ('pronto') {
        <h1>Campeonatos de {{ dadosDaOrganizacao()!.name }}</h1>

        @if (proprietario()) {
          <a
            class="acao acao--principal"
            [routerLink]="['/organizar/o', organizacao(), 'campeonatos', 'novo']"
            >Criar campeonato</a
          >
        }

        <app-card heading="Campeonatos">
          @if (campeonatos().length > 0) {
            <ul class="lista">
              @for (item of campeonatos(); track item.id) {
                <li class="lista__item">
                  <a class="lista__principal" [routerLink]="['/organizar/c', item.id]">
                    {{ item.name }}
                  </a>
                  <app-badge [tone]="item.status === 'Draft' ? 'warning' : 'success'">
                    {{ status(item) }}
                  </app-badge>
                  <span class="lista__detalhe">
                    {{ modalidade(item) }} · temporada {{ item.season }} · alterado em
                    {{ item.updatedAt | date: formatoData }}
                  </span>
                </li>
              }
            </ul>
          } @else if (proprietario()) {
            <p class="apoio">
              Nenhum campeonato ainda. Crie o primeiro em rascunho: só a organização vê até a
              publicação.
            </p>
          } @else {
            <p class="apoio">
              Nenhum campeonato ainda. Quem é proprietário da organização cria os campeonatos.
            </p>
          }
        </app-card>
      }
    }
  `,
  styleUrl: './organizer.scss',
})
export class OrganizationCompetitionsPage implements OnInit {
  private readonly organizations = inject(OrganizationService);
  private readonly competitions = inject(CompetitionService);

  /** Identificador da organização na rota. */
  readonly organizacao = input.required<string>();

  protected readonly formatoData = 'd MMM y';
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly dadosDaOrganizacao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.organizacao : null;
  });

  protected readonly campeonatos = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonatos : [];
  });

  protected readonly proprietario = computed(() => this.dadosDaOrganizacao()?.role === 'Owner');

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected status(item: CompetitionSummary): string {
    return STATUS_LABELS[item.status];
  }

  protected modalidade(item: CompetitionSummary): string {
    return MODALITY_LABELS[item.modality];
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    forkJoin({
      organizacao: this.organizations.get(this.organizacao()),
      campeonatos: this.competitions.listByOrganization(this.organizacao()),
    }).subscribe({
      next: ({ organizacao, campeonatos }) =>
        this.estado.set({ tipo: 'pronto', organizacao, campeonatos }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
