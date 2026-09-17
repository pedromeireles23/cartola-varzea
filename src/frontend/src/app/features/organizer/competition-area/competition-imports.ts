import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  IMPORT_INVALID_CODE,
  IMPORT_REJECTED_CODE,
  ImportIssue,
  ImportKind,
  ImportResult,
  ImportService,
  ImportTemplate,
  KIND_BY_DOMAIN,
  KIND_LABELS,
  MAX_FILE_BYTES,
} from './import.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly modelos: readonly ImportTemplate[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** O que a tela mostra depois de conferir ou importar um arquivo. */
type Retorno =
  | { readonly tipo: 'previa'; readonly resultado: ImportResult }
  | { readonly tipo: 'importado'; readonly resultado: ImportResult }
  | { readonly tipo: 'recusado'; readonly mensagem: string }
  | { readonly tipo: 'invalido'; readonly problemas: readonly ImportIssue[] };

/**
 * Importação CSV do catálogo (02 §9.1, `/organizar/c/:campeonato/importacoes`).
 *
 * O fluxo é sempre modelo → conferir → importar. "Conferir" não grava nada; o mesmo
 * arquivo é enviado de novo na importação, e o servidor revalida do zero. O navegador
 * é quem guarda o arquivo entre as duas etapas, porque o servidor não guarda (ADR-009).
 */
@Component({
  selector: 'app-competition-imports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading],
  template: `
    <div class="pagina">
      <h1>Importações</h1>
      <p class="intro">
        Traga times, atletas e técnicos de uma planilha. Importe nesta ordem: o atleta e o técnico
        apontam para o time pelo nome.
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

              <div class="tabela">
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
            <app-card heading="Enviar arquivo">
              <p class="apoio">
                Salve a planilha como CSV UTF-8, de até {{ tamanhoMax }} KB. Nenhum arquivo fica
                guardado no servidor.
              </p>

              <label class="arquivo">
                <span class="arquivo__rotulo">Arquivo CSV</span>
                <input type="file" accept=".csv,text/csv" (change)="escolher($event)" #entrada />
              </label>

              @if (arquivo(); as escolhido) {
                <p class="apoio">Escolhido: {{ escolhido.name }}</p>
              }

              <div class="acoes">
                <app-button
                  variant="secondary"
                  [disabled]="arquivo() === null"
                  [loading]="ocupado() === 'previa'"
                  (pressed)="conferir()"
                >
                  Conferir arquivo
                </app-button>
                <app-button
                  [disabled]="arquivo() === null || !conferido()"
                  [loading]="ocupado() === 'importacao'"
                  (pressed)="importar()"
                >
                  Importar
                </app-button>
              </div>
              @if (arquivo() !== null && !conferido()) {
                <p class="apoio">Confira o arquivo antes de importar.</p>
              }
            </app-card>
          }

          <div class="foco" tabindex="-1" #aviso>
            @if (retorno(); as resultado) {
              @switch (resultado.tipo) {
                @case ('recusado') {
                  <app-card heading="Arquivo recusado">
                    <app-alert tone="danger">{{ resultado.mensagem }}</app-alert>
                  </app-card>
                }
                @case ('invalido') {
                  <app-card heading="Linhas para corrigir">
                    <app-alert tone="danger">
                      Nada foi gravado. Corrija as linhas abaixo e envie o arquivo de novo.
                    </app-alert>
                    <ul class="problemas">
                      @for (problema of resultado.problemas; track $index) {
                        <li class="problema">
                          <app-badge tone="neutral">
                            {{ problema.line === 1 ? 'Cabeçalho' : 'Linha ' + problema.line }}
                          </app-badge>
                          <span>{{ problema.message }}</span>
                        </li>
                      }
                    </ul>
                  </app-card>
                }
                @default {
                  <app-card [heading]="resultado.tipo === 'previa' ? 'Prévia' : 'Importado'">
                    <app-alert [tone]="resultado.tipo === 'previa' ? 'info' : 'success'">
                      {{ resumo(resultado.resultado, resultado.tipo === 'previa') }}
                    </app-alert>
                    @if (resultado.tipo === 'previa') {
                      <p class="apoio">Nada foi gravado ainda. Use "Importar" para aplicar.</p>
                    }
                  </app-card>
                }
              }
            }
          </div>
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-imports.scss'],
})
export class CompetitionImportsPage {
  private readonly service = inject(ImportService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');
  private readonly entrada = viewChild<ElementRef<HTMLInputElement>>('entrada');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly abas: readonly ImportKind[] = ['times', 'atletas', 'tecnicos'];
  protected readonly tamanhoMax = MAX_FILE_BYTES / 1024;

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly tipo = signal<ImportKind>('times');
  protected readonly arquivo = signal<File | null>(null);
  protected readonly ocupado = signal<'previa' | 'importacao' | null>(null);
  protected readonly retorno = signal<Retorno | null>(null);

  /** Só libera importar depois de uma prévia sem erro do arquivo escolhido. */
  protected readonly conferido = signal(false);

  protected readonly modelo = computed(() => {
    const atual = this.estado();
    if (atual.tipo !== 'pronto') return null;
    return atual.modelos.find((item) => KIND_BY_DOMAIN[item.kind] === this.tipo()) ?? null;
  });

  protected readonly modeloUrl = computed(() =>
    this.service.templateUrl(this.campeonatoId, this.tipo()),
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
    if (this.tipo() === kind) return;
    this.tipo.set(kind);
    this.limpar();
  }

  protected escolher(event: Event): void {
    const escolhido = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.retorno.set(null);
    this.conferido.set(false);
    if (escolhido && escolhido.size > MAX_FILE_BYTES) {
      this.arquivo.set(null);
      this.avisar({
        tipo: 'recusado',
        mensagem: `O arquivo passa de ${this.tamanhoMax} KB. Divida a planilha em partes.`,
      });
      return;
    }

    this.arquivo.set(escolhido);
  }

  protected conferir(): void {
    this.enviar('previa');
  }

  protected importar(): void {
    this.enviar('importacao');
  }

  private enviar(acao: 'previa' | 'importacao'): void {
    const escolhido = this.arquivo();
    if (!escolhido || this.ocupado() !== null) return;

    this.ocupado.set(acao);
    const pedido =
      acao === 'previa'
        ? this.service.preview(this.campeonatoId, this.tipo(), escolhido)
        : this.service.commit(this.campeonatoId, this.tipo(), escolhido);

    pedido.subscribe({
      next: (resultado) => {
        this.ocupado.set(null);
        if (acao === 'previa') {
          this.conferido.set(true);
          this.avisar({ tipo: 'previa', resultado });
          return;
        }

        this.limpar();
        this.avisar({ tipo: 'importado', resultado });
      },
      error: (falha: ApiFailure) => this.tratar(falha),
    });
  }

  private tratar(falha: ApiFailure): void {
    this.ocupado.set(null);
    this.conferido.set(false);
    if (falha.code === IMPORT_INVALID_CODE) {
      this.avisar({ tipo: 'invalido', problemas: problemas(falha) });
      return;
    }

    if (falha.code === IMPORT_REJECTED_CODE) {
      this.avisar({
        tipo: 'recusado',
        mensagem: (falha.problem?.['detail'] as string | undefined) ?? falha.message,
      });
      return;
    }

    this.avisar({ tipo: 'recusado', mensagem: falha.message });
  }

  private limpar(): void {
    this.arquivo.set(null);
    this.conferido.set(false);
    const entrada = this.entrada()?.nativeElement;
    if (entrada) entrada.value = '';
  }

  private avisar(retorno: Retorno): void {
    this.retorno.set(retorno);
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }

  protected resumo(resultado: ImportResult, previa: boolean): string {
    const { rows, created, updated, unchanged } = resultado.summary;
    if (rows === 0) {
      return 'O arquivo não tem nenhuma linha de dados.';
    }

    const partes = [
      `${created} ${previa ? 'a criar' : created === 1 ? 'criado' : 'criados'}`,
      `${updated} ${previa ? 'a alterar' : updated === 1 ? 'alterado' : 'alterados'}`,
      `${unchanged} sem mudança`,
    ];
    return `${rows} ${rows === 1 ? 'linha lida' : 'linhas lidas'}: ${partes.join(', ')}.`;
  }
}

/** Lê a lista de problemas do Problem Details, conferindo o formato. */
function problemas(falha: ApiFailure): readonly ImportIssue[] {
  const bruto = falha.problem?.['issues'];
  if (!Array.isArray(bruto)) return [];
  return bruto.filter(
    (item): item is ImportIssue =>
      typeof item === 'object' &&
      item !== null &&
      typeof (item as ImportIssue).line === 'number' &&
      typeof (item as ImportIssue).message === 'string',
  );
}
