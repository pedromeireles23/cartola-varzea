import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../shared/ui';
import { MyOrganization, OrganizationService } from './organization.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly organizacoes: readonly MyOrganization[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Organizações da conta (02 §9.1, `/organizar`).
 *
 * O papel vem da associação com cada organização, não de um papel global: a mesma
 * conta pode ser proprietária de uma liga e auxiliar de outra.
 */
@Component({
  selector: 'app-my-organizations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, DatePipe, Loading, RouterLink],
  template: `
    <h1>Minhas organizações</h1>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Buscando suas organizações…" />
        </app-card>
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
        @for (item of organizacoes(); track item.id) {
          <app-card [heading]="item.name">
            <dl class="dados">
              <div class="dados__item">
                <dt>Seu papel</dt>
                <dd>
                  @if (item.role === 'Owner') {
                    <app-badge tone="brand">Proprietário</app-badge>
                  } @else {
                    <app-badge>Auxiliar</app-badge>
                  }
                </dd>
              </div>
              <div class="dados__item">
                <dt>Desde</dt>
                <dd>{{ item.joinedAt | date: formatoData }}</dd>
              </div>
            </dl>

            <div class="acoes">
              <a class="acao" [routerLink]="['/organizar/o', item.id, 'campeonatos']">
                Campeonatos
              </a>
              @if (item.role === 'Owner') {
                <a class="acao" [routerLink]="['/organizar/o', item.id, 'equipe']">
                  Gerenciar equipe
                </a>
              }
            </div>
          </app-card>
        } @empty {
          <app-card heading="Nenhuma organização ainda">
            <p class="apoio">
              Para organizar campeonatos, peça acesso. Se recebeu um convite para auxiliar, abra o
              link que chegou por e-mail.
            </p>
            <a class="acao" routerLink="/organizar/solicitar">Solicitar acesso</a>
          </app-card>
        }

        @if (organizacoes().length > 0) {
          <p class="rodape">
            Organiza outra liga? <a routerLink="/organizar/solicitar">Peça acesso para ela</a>.
          </p>
        }
      }
    }
  `,
  styleUrl: './organizer.scss',
})
export class MyOrganizationsPage {
  private readonly service = inject(OrganizationService);

  protected readonly formatoData = 'd MMM y';
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly organizacoes = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.organizacoes : [];
  });

  constructor() {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    this.service.mine().subscribe({
      next: (organizacoes) => this.estado.set({ tipo: 'pronto', organizacoes }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
