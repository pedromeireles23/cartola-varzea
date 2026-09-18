import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card } from '../../../shared/ui';
import {
  IMPORT_INVALID_CODE,
  IMPORT_REJECTED_CODE,
  ImportIssue,
  ImportResult,
  ImportService,
  MAX_FILE_BYTES,
} from './import.service';

/** O que a tela mostra depois de conferir ou importar um arquivo. */
type Retorno =
  | { readonly tipo: 'previa'; readonly resultado: ImportResult }
  | { readonly tipo: 'importado'; readonly resultado: ImportResult }
  | { readonly tipo: 'recusado'; readonly mensagem: string }
  | { readonly tipo: 'invalido'; readonly problemas: readonly ImportIssue[] };

/**
 * Envio de um CSV: escolher, conferir e importar (ADR-009).
 *
 * "Conferir" não grava nada; o mesmo arquivo é enviado de novo na importação, e o
 * servidor revalida do zero. O navegador é quem guarda o arquivo entre as duas etapas,
 * porque o servidor não guarda. Serve ao catálogo, às partidas e às estatísticas da
 * rodada, que só diferem nos endereços.
 */
@Component({
  selector: 'app-import-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card],
  template: `
    <app-card heading="Enviar arquivo">
      <p class="apoio">
        Salve a planilha como CSV UTF-8, de até {{ tamanhoMax }} KB. Nenhum arquivo fica guardado no
        servidor.
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
          (pressed)="enviar('previa')"
        >
          Conferir arquivo
        </app-button>
        <app-button
          [disabled]="arquivo() === null || !conferido()"
          [loading]="ocupado() === 'importacao'"
          (pressed)="enviar('importacao')"
        >
          Importar
        </app-button>
      </div>
      @if (arquivo() !== null && !conferido()) {
        <p class="apoio">Confira o arquivo antes de importar.</p>
      }
    </app-card>

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
              @for (nota of resultado.resultado.notes ?? []; track $index) {
                <p class="nota">{{ nota }}</p>
              }
              @if (resultado.tipo === 'previa') {
                <p class="apoio">Nada foi gravado ainda. Use "Importar" para aplicar.</p>
              }
            </app-card>
          }
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './import-upload.scss'],
})
export class ImportUpload {
  private readonly service = inject(ImportService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');
  private readonly entrada = viewChild<ElementRef<HTMLInputElement>>('entrada');

  /** Endereço da conferência, que lê o arquivo sem gravar. */
  readonly previewUrl = input.required<string>();

  /** Endereço da importação, que revalida e grava tudo ou nada. */
  readonly commitUrl = input.required<string>();

  /** O que as contagens do resumo contam: registros do catálogo ou súmulas da rodada. */
  readonly contagem = input<'registros' | 'sumulas'>('registros');

  /** Avisa quem está em volta para recarregar o que o arquivo mudou. */
  readonly importado = output<ImportResult>();

  protected readonly tamanhoMax = MAX_FILE_BYTES / 1024;
  protected readonly arquivo = signal<File | null>(null);
  protected readonly ocupado = signal<'previa' | 'importacao' | null>(null);
  protected readonly retorno = signal<Retorno | null>(null);

  /** Só libera importar depois de uma prévia sem erro do arquivo escolhido. */
  protected readonly conferido = signal(false);

  constructor() {
    // Outro destino (outra aba, outro modelo) começa do zero: a prévia de um arquivo
    // não vale como conferência de outro tipo de importação.
    effect(() => {
      this.previewUrl();
      untracked(() => {
        this.limpar();
        this.retorno.set(null);
      });
    });
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

  protected enviar(acao: 'previa' | 'importacao'): void {
    const escolhido = this.arquivo();
    if (!escolhido || this.ocupado() !== null) return;

    this.ocupado.set(acao);
    const url = acao === 'previa' ? this.previewUrl() : this.commitUrl();
    this.service.send(url, escolhido).subscribe({
      next: (resultado) => {
        this.ocupado.set(null);
        if (acao === 'previa') {
          this.conferido.set(true);
          this.avisar({ tipo: 'previa', resultado });
          return;
        }

        this.limpar();
        this.avisar({ tipo: 'importado', resultado });
        this.importado.emit(resultado);
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

    const partes =
      this.contagem() === 'sumulas'
        ? [
            `${created} ${created === 1 ? 'súmula nova' : 'súmulas novas'}`,
            `${updated} ${previa ? 'a substituir' : updated === 1 ? 'substituída' : 'substituídas'}`,
            `${unchanged} sem mudança`,
          ]
        : [
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
