import { DatePipe } from '@angular/common';
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

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, Dialog, FormField, Loading } from '../../shared/ui';
import {
  OrganizerReviewService,
  PendingOrganizerApplication,
  REVIEW_REASON_MAX,
  REVIEW_REASON_MIN,
  ReviewDecision,
} from './organizer-review.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly fila: readonly PendingOrganizerApplication[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

interface Analise {
  readonly solicitacao: PendingOrganizerApplication;
  readonly decisao: ReviewDecision;
}

/**
 * Fila de solicitações de organizador do Platform admin (02 §9.1, `/admin/solicitacoes`).
 *
 * Aprovar e não aprovar pedem motivo e confirmação num diálogo, porque a decisão não
 * se desfaz. O diálogo diz a quem decide quem vai ler o motivo: numa recusa, a pessoa
 * que pediu; numa aprovação, só a auditoria.
 */
@Component({
  selector: 'app-organizer-applications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, DatePipe, Dialog, FormField, Loading],
  template: `
    <h1>Solicitações de organizador</h1>
    <p class="intro">
      Aprovar cria a organização e dá acesso de organizador a quem pediu. Cada decisão fica
      registrada na auditoria.
    </p>

    <div class="foco" tabindex="-1" #retorno>
      @if (retornoDaDecisao(); as retorno) {
        <app-alert [tone]="retorno.tom">{{ retorno.texto }}</app-alert>
      }
    </div>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Buscando solicitações…" />
        </app-card>
      }

      @case ('erro') {
        <app-card>
          @if (falhaAoCarregar()!.status === 403) {
            <app-alert tone="warning">
              Esta área é exclusiva da administração da plataforma.
            </app-alert>
          } @else {
            <app-alert tone="danger">
              <p>{{ falhaAoCarregar()!.message }}</p>
              @if (falhaAoCarregar()!.traceId) {
                <p class="trace">Código de rastreio: {{ falhaAoCarregar()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }

      @case ('pronto') {
        @for (item of fila(); track item.id) {
          <app-card [heading]="item.organizationName">
            <dl class="dados">
              <div class="dados__item">
                <dt>Quem pediu</dt>
                <dd>{{ item.applicantDisplayName }}</dd>
              </div>
              <div class="dados__item">
                <dt>E-mail</dt>
                <dd>{{ item.applicantEmail }}</dd>
              </div>
              <div class="dados__item">
                <dt>Enviada em</dt>
                <dd>{{ item.submittedAt | date: formatoData }}</dd>
              </div>
            </dl>

            <div class="acoes">
              <app-button escrita (pressed)="abrir(item, 'approve')">Aprovar</app-button>
              <app-button escrita variant="secondary" (pressed)="abrir(item, 'reject')">
                Não aprovar
              </app-button>
            </div>
          </app-card>
        } @empty {
          <app-card>
            <p class="vazio">Nenhuma solicitação aguardando análise.</p>
          </app-card>
        }
      }
    }

    <app-dialog [heading]="tituloDoDialogo()" [open]="analise() !== null" (dismissed)="fechar()">
      @if (analise(); as atual) {
        <div class="dialogo">
          @if (atual.decisao === 'approve') {
            <p>
              A organização <strong>{{ atual.solicitacao.organizationName }}</strong> é criada e
              <strong>{{ atual.solicitacao.applicantDisplayName }}</strong> passa a administrá-la
            </p>
            <p class="aviso">O motivo fica só na auditoria; quem pediu não vê.</p>
          } @else {
            <p>
              <strong>{{ atual.solicitacao.applicantDisplayName }}</strong> poderá ajustar o pedido
              e enviar de novo.
            </p>
            <!-- Fora da dica do campo: o erro de validação substitui a dica, e este aviso -->
            <!-- não pode sumir justamente quando a pessoa está reescrevendo o motivo. -->
            <p class="aviso">Quem pediu vai ler este motivo. Diga o que falta para aprovar.</p>
          }

          @if (falhaAoDecidir()) {
            <app-alert tone="danger">{{ falhaAoDecidir() }}</app-alert>
          }

          <app-form-field
            label="Motivo"
            [multiline]="true"
            [required]="true"
            [minLength]="minimo"
            [maxLength]="maximo"
            [error]="erroDoMotivo() ?? undefined"
            [(value)]="motivo"
          />
        </div>
      }

      <div dialogActions class="dialogo__acoes">
        <app-button variant="ghost" [disabled]="decidindo()" (pressed)="fechar()">
          Cancelar
        </app-button>
        <app-button
          escrita
          [variant]="analise()?.decisao === 'reject' ? 'danger' : 'primary'"
          [loading]="decidindo()"
          (pressed)="confirmar()"
        >
          {{ analise()?.decisao === 'reject' ? 'Confirmar recusa' : 'Confirmar aprovação' }}
        </app-button>
      </div>
    </app-dialog>
  `,
  styleUrl: './organizer-applications.scss',
})
export class OrganizerApplicationsPage {
  private readonly service = inject(OrganizerReviewService);
  private readonly injector = inject(Injector);

  protected readonly minimo = REVIEW_REASON_MIN;
  protected readonly maximo = REVIEW_REASON_MAX;
  protected readonly formatoData = "d MMM y 'às' HH'h'mm";

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly analise = signal<Analise | null>(null);
  protected readonly motivo = signal('');
  protected readonly erroDoMotivo = signal<string | null>(null);
  protected readonly falhaAoDecidir = signal<string | null>(null);
  protected readonly decidindo = signal(false);
  protected readonly retornoDaDecisao = signal<{
    readonly tom: 'success' | 'warning';
    readonly texto: string;
  } | null>(null);

  private readonly retorno = viewChild.required<ElementRef<HTMLElement>>('retorno');

  protected readonly fila = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.fila : [];
  });

  protected readonly tituloDoDialogo = computed(() => {
    const atual = this.analise();
    if (!atual) {
      return '';
    }

    return atual.decisao === 'approve'
      ? `Aprovar ${atual.solicitacao.organizationName}?`
      : `Não aprovar ${atual.solicitacao.organizationName}?`;
  });

  constructor() {
    this.carregar();
  }

  protected falhaAoCarregar(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    this.service.pending().subscribe({
      next: (fila) => this.estado.set({ tipo: 'pronto', fila }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrir(solicitacao: PendingOrganizerApplication, decisao: ReviewDecision): void {
    this.motivo.set('');
    this.erroDoMotivo.set(null);
    this.falhaAoDecidir.set(null);
    this.analise.set({ solicitacao, decisao });
  }

  protected fechar(): void {
    if (!this.decidindo()) {
      this.analise.set(null);
    }
  }

  protected confirmar(): void {
    const atual = this.analise();
    if (!atual || this.decidindo()) {
      return;
    }

    const motivo = this.motivo().trim();
    if (motivo.length < REVIEW_REASON_MIN || motivo.length > REVIEW_REASON_MAX) {
      this.erroDoMotivo.set(
        `Escreva um motivo entre ${REVIEW_REASON_MIN} e ${REVIEW_REASON_MAX} caracteres.`,
      );
      return;
    }

    this.erroDoMotivo.set(null);
    this.falhaAoDecidir.set(null);
    this.decidindo.set(true);

    const nome = atual.solicitacao.organizationName;
    this.service.decide(atual.solicitacao.id, atual.decisao, motivo).subscribe({
      next: () => {
        this.concluir(
          atual.solicitacao.id,
          atual.decisao === 'approve'
            ? { tom: 'success', texto: `${nome} foi aprovada. A organização já existe.` }
            : { tom: 'success', texto: `A solicitação de ${nome} não foi aprovada.` },
        );
      },
      error: (falha: ApiFailure) => {
        if (falha.status === 409 || falha.status === 404) {
          // Outra pessoa decidiu primeiro. Não há o que corrigir no formulário: a fila
          // é recarregada para mostrar o estado real.
          this.concluir(atual.solicitacao.id, {
            tom: 'warning',
            texto: `A solicitação de ${nome} já tinha sido decidida. A lista foi atualizada.`,
          });
          this.carregar();
          return;
        }

        this.falhaAoDecidir.set(falha.message);
        this.decidindo.set(false);
      },
    });
  }

  private concluir(
    id: string,
    retorno: { readonly tom: 'success' | 'warning'; readonly texto: string },
  ): void {
    this.decidindo.set(false);
    this.analise.set(null);
    this.estado.set({ tipo: 'pronto', fila: this.fila().filter((item) => item.id !== id) });
    this.retornoDaDecisao.set(retorno);

    // O cartão de origem saiu da lista; o foco vai para o aviso do resultado.
    afterNextRender(() => this.retorno().nativeElement.focus(), { injector: this.injector });
  }
}
