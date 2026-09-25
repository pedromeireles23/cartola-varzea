import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Button, Card, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  ImportKind,
  ImportService,
  ImportTemplate,
  KIND_BY_DOMAIN,
  KIND_LABELS,
} from './import.service';
import { ImportUpload } from './import-upload';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly modelos: readonly ImportTemplate[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Importação CSV do catálogo e das partidas (02 §9.1, `/organizar/c/:campeonato/importacoes`).
 *
 * O fluxo é sempre modelo → conferir → importar; o envio fica em `ImportUpload`, que a
 * conferência da rodada também usa para as estatísticas.
 */
@Component({
  selector: 'app-competition-imports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, ImportUpload, Loading],
  template: `
    <div class="pagina">
      <h1>Importações</h1>
      <p class="intro">
        Traga times, atletas, técnicos e partidas de uma planilha. Importe nesta ordem: atleta e
        técnico apontam para o time pelo nome, e a partida precisa da fase com os times confirmados.
        As estatísticas de cada jogo são importadas na conferência da rodada.
      </p>

      @if (!proprietario()) {
        <app-card>
          <app-alert tone="warning">
            Somente quem é proprietário da organização importa. Você pode baixar os modelos.
          </app-alert>
        </app-card>
      }

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando os modelos…" /></app-card>
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
          <app-card>
            <div class="abas" role="tablist" aria-label="O que importar">
              @for (aba of abas; track aba) {
                <button
                  type="button"
                  role="tab"
                  class="aba"
                  [class.aba--ativa]="tipo() === aba"
                  [attr.aria-selected]="tipo() === aba"
                  (click)="trocar(aba)"
                >
                  {{ rotulo(aba) }}
                </button>
              }
            </div>

            @if (modelo(); as atual) {
              <p class="apoio">Modelo {{ atual.fileName }}, versão {{ atual.version }}.</p>

              <!-- Rolável no celular: foco e nome para quem navega por teclado (axe). -->
              <div
                class="tabela"
                tabindex="0"
                role="region"
                [attr.aria-label]="'Colunas de ' + atual.label"
              >
                <table>
                  <caption>
                    Colunas de
                    {{
                      atual.label
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">Coluna</th>
                      <th scope="col">Obrigatória</th>
                      <th scope="col">O que preencher</th>
                      <th scope="col">Exemplo</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (coluna of atual.columns; track coluna.name) {
                      <tr>
                        <th scope="row">
                          <code>{{ coluna.name }}</code>
                        </th>
                        <td>{{ coluna.required ? 'sim' : 'não' }}</td>
                        <td>{{ coluna.description }}</td>
                        <td>{{ coluna.example || '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>

              <a class="acao" [href]="modeloUrl()" [download]="atual.fileName">
                Baixar modelo de {{ atual.label }}
              </a>
            }
          </app-card>

          @if (proprietario()) {
            <app-import-upload [previewUrl]="previewUrl()" [commitUrl]="commitUrl()" />
          }
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-imports.scss'],
})
export class CompetitionImportsPage {
  private readonly service = inject(ImportService);

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly abas: readonly ImportKind[] = ['times', 'atletas', 'tecnicos', 'partidas'];

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly tipo = signal<ImportKind>('times');

  protected readonly modelo = computed(() => {
    const atual = this.estado();
    if (atual.tipo !== 'pronto') return null;
    return atual.modelos.find((item) => KIND_BY_DOMAIN[item.kind] === this.tipo()) ?? null;
  });

  protected readonly modeloUrl = computed(() =>
    this.service.templateUrl(this.campeonatoId, this.tipo()),
  );

  protected readonly previewUrl = computed(() =>
    this.service.previewUrl(this.campeonatoId, this.tipo()),
  );

  protected readonly commitUrl = computed(() =>
    this.service.commitUrl(this.campeonatoId, this.tipo()),
  );

  constructor() {
    this.carregar();
  }

  private get campeonatoId(): string {
    return this.contexto.campeonato()?.id ?? '';
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected rotulo(kind: ImportKind): string {
    return KIND_LABELS[kind];
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });
    this.service.templates().subscribe({
      next: (modelos) => this.estado.set({ tipo: 'pronto', modelos }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected trocar(kind: ImportKind): void {
    this.tipo.set(kind);
  }
}
