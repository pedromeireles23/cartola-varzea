import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../shared/ui';
import { SystemInfo, SystemInfoService } from './system-info.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'sucesso'; readonly dados: SystemInfo }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Corte vertical da Fase 2: consome GET /api/v1/system/info e mostra o resultado.
 *
 * Existe para provar Angular -> API -> SQL Server de ponta a ponta, e cobre os
 * estados obrigatorios de tela do 02 §10: carregando, sucesso e erro recuperavel.
 */
@Component({
  selector: 'app-system-info',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, DatePipe, Loading],
  template: `
    <h1>Estado do sistema</h1>
    <p class="intro">
      Primeira leitura ponta a ponta: a página consulta a API, que consulta o SQL Server.
    </p>

    <app-card heading="Informação técnica">
      @switch (estado().tipo) {
        @case ('carregando') {
          <app-loading label="Consultando a API…" />
        }

        @case ('erro') {
          <app-alert tone="danger">
            <p>{{ erro()!.message }}</p>
            @if (erro()!.traceId) {
              <p class="trace">Código de rastreio: {{ erro()!.traceId }}</p>
            }
          </app-alert>
          <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
        }

        @case ('sucesso') {
          <dl class="dados">
            <div class="dados__item">
              <dt>Versão</dt>
              <dd>{{ dados()!.version }}</dd>
            </div>
            <div class="dados__item">
              <dt>Ambiente</dt>
              <dd>
                <app-badge tone="brand">{{ dados()!.environment }}</app-badge>
              </dd>
            </div>
            <div class="dados__item">
              <dt>Hora do servidor</dt>
              <dd>{{ dados()!.serverTimeUtc | date: 'dd MMM yyyy, HH:mm:ss' : 'UTC' }} UTC</dd>
            </div>
            <div class="dados__item">
              <dt>Inicializações registradas</dt>
              <dd>{{ dados()!.startupCount }}</dd>
            </div>
            <div class="dados__item">
              <dt>Última inicialização</dt>
              <dd>
                @if (dados()!.lastStartedAt) {
                  {{ dados()!.lastStartedAt | date: 'dd MMM yyyy, HH:mm:ss' : 'UTC' }} UTC
                } @else {
                  Nenhuma registrada ainda
                }
              </dd>
            </div>
          </dl>
        }
      }
    </app-card>
  `,
  styleUrl: './system-info.scss',
})
export class SystemInfoPage {
  private readonly service = inject(SystemInfoService);

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  constructor() {
    this.carregar();
  }

  protected dados(): SystemInfo | null {
    const atual = this.estado();
    return atual.tipo === 'sucesso' ? atual.dados : null;
  }

  protected erro(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    this.service.get().subscribe({
      next: (dados) => this.estado.set({ tipo: 'sucesso', dados }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }
}
