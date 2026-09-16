import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, BadgeTone, Button, Card, FormField, Loading } from '../../shared/ui';
import {
  ORGANIZATION_NAME_MAX,
  ORGANIZATION_NAME_MIN,
  OrganizerApplication,
  OrganizerApplicationService,
  OrganizerApplicationStatus,
} from './organizer-application.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly solicitacoes: readonly OrganizerApplication[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

const STATUS: Readonly<
  Record<OrganizerApplicationStatus, { readonly rotulo: string; readonly tom: BadgeTone }>
> = {
  Pending: { rotulo: 'Em análise', tom: 'warning' },
  Approved: { rotulo: 'Aprovada', tom: 'success' },
  Rejected: { rotulo: 'Não aprovada', tom: 'danger' },
};

/**
 * Solicitação de acesso de organizador e acompanhamento (02 §9.1, `/organizar/solicitar`).
 *
 * Existe no máximo uma solicitação em análise por conta; enquanto ela existe, a tela
 * mostra o acompanhamento no lugar do formulário. O motivo só aparece quando a
 * solicitação não foi aprovada: é a informação de que a pessoa precisa para tentar
 * de novo.
 */
@Component({
  selector: 'app-request-access',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, DatePipe, FormField, Loading, RouterLink],
  template: `
    <h1>Organizar campeonatos</h1>
    <p class="intro">
      Para cadastrar campeonatos, times e súmulas, sua conta precisa de uma organização aprovada. A
      equipe da plataforma analisa cada pedido.
    </p>

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Buscando suas solicitações…" />
        </app-card>
      }

      @case ('erro') {
        <app-card>
          <app-alert tone="danger">
            <p>{{ falhaAoCarregar()!.message }}</p>
            @if (falhaAoCarregar()!.traceId) {
              <p class="trace">Código de rastreio: {{ falhaAoCarregar()!.traceId }}</p>
            }
          </app-alert>
          <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
        </app-card>
      }

      @case ('pronto') {
        @if (emAnalise(); as pedido) {
          <div class="foco" tabindex="-1" #acompanhamento>
            <app-card heading="Sua solicitação">
              @if (acabouDeEnviar()) {
                <app-alert tone="success">Solicitação enviada.</app-alert>
              }

              <dl class="dados">
                <div class="dados__item">
                  <dt>Organização</dt>
                  <dd>{{ pedido.organizationName }}</dd>
                </div>
                <div class="dados__item">
                  <dt>Situação</dt>
                  <dd>
                    <app-badge [tone]="status(pedido).tom">{{ status(pedido).rotulo }}</app-badge>
                  </dd>
                </div>
                <div class="dados__item">
                  <dt>Enviada em</dt>
                  <dd>{{ pedido.submittedAt | date: formatoData }}</dd>
                </div>
              </dl>

              <p class="apoio">
                A decisão aparece aqui. Não é preciso enviar de novo enquanto o pedido está em
                análise.
              </p>
            </app-card>
          </div>
        } @else {
          @if (ultimaDecisao(); as decisao) {
            @if (decisao.status === 'Rejected') {
              <app-alert tone="warning">
                <p>
                  Sua solicitação para <strong>{{ decisao.organizationName }}</strong> não foi
                  aprovada.
                </p>
                @if (decisao.decisionReason) {
                  <p>Motivo informado: {{ decisao.decisionReason }}</p>
                }
                <p>Você pode ajustar o pedido e enviar de novo.</p>
              </app-alert>
            } @else {
              <app-alert tone="success">
                <p>
                  A organização <strong>{{ decisao.organizationName }}</strong> foi aprovada.
                </p>
                <p>
                  <a routerLink="/organizar">Ver minhas organizações</a>. Se quiser organizar outra
                  liga, envie um novo pedido abaixo.
                </p>
              </app-alert>
            }
          }

          <app-card [heading]="ultimaDecisao() ? 'Enviar nova solicitação' : 'Solicitar acesso'">
            <form (submit)="enviar($event)" novalidate>
              @if (falhaAoEnviar()) {
                <app-alert tone="danger">{{ falhaAoEnviar() }}</app-alert>
              }

              <app-form-field
                label="Nome da organização"
                autocomplete="organization"
                [hint]="dica"
                [required]="true"
                [minLength]="minimo"
                [maxLength]="maximo"
                [error]="erroDoNome() ?? undefined"
                [(value)]="nome"
              />

              <app-button type="submit" [loading]="enviando()" [fullWidth]="true">
                Enviar solicitação
              </app-button>
            </form>
          </app-card>
        }

        @if (historico().length > 0) {
          <app-card heading="Histórico">
            <ul class="historico">
              @for (item of historico(); track item.id) {
                <li class="historico__item">
                  <span class="historico__nome">{{ item.organizationName }}</span>
                  <app-badge [tone]="status(item).tom">{{ status(item).rotulo }}</app-badge>
                  <span class="historico__data">
                    Enviada em {{ item.submittedAt | date: formatoData }}
                  </span>
                </li>
              }
            </ul>
          </app-card>
        }
      }
    }
  `,
  styleUrl: './request-access.scss',
})
export class RequestAccessPage {
  private readonly service = inject(OrganizerApplicationService);
  private readonly injector = inject(Injector);

  protected readonly minimo = ORGANIZATION_NAME_MIN;
  protected readonly maximo = ORGANIZATION_NAME_MAX;
  protected readonly dica = `Como a liga ou o grupo que organiza é conhecido. De ${ORGANIZATION_NAME_MIN} a ${ORGANIZATION_NAME_MAX} caracteres.`;
  protected readonly formatoData = "d MMM y 'às' HH'h'mm";

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly nome = signal('');
  protected readonly erroDoNome = signal<string | null>(null);
  protected readonly enviando = signal(false);
  protected readonly falhaAoEnviar = signal<string | null>(null);
  protected readonly acabouDeEnviar = signal(false);

  private readonly acompanhamento = viewChild<ElementRef<HTMLElement>>('acompanhamento');

  private readonly solicitacoes = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.solicitacoes : [];
  });

  protected readonly emAnalise = computed(
    () => this.solicitacoes().find((item) => item.status === 'Pending') ?? null,
  );

  protected readonly ultimaDecisao = computed(
    () => this.solicitacoes().find((item) => item.status !== 'Pending') ?? null,
  );

  /** Pedidos já decididos, para a pessoa entender o que aconteceu antes. */
  protected readonly historico = computed(() =>
    this.solicitacoes().filter((item) => item.status !== 'Pending'),
  );

  constructor() {
    this.carregar();
  }

  protected falhaAoCarregar(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected status(item: OrganizerApplication): { rotulo: string; tom: BadgeTone } {
    return STATUS[item.status];
  }

  protected carregar(): void {
    this.estado.set({ tipo: 'carregando' });

    this.service.mine().subscribe({
      next: (solicitacoes) => this.estado.set({ tipo: 'pronto', solicitacoes }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected enviar(event: Event): void {
    event.preventDefault();
    this.falhaAoEnviar.set(null);

    const nome = this.nome().trim();
    if (nome.length < ORGANIZATION_NAME_MIN || nome.length > ORGANIZATION_NAME_MAX) {
      this.erroDoNome.set(
        `Informe um nome entre ${ORGANIZATION_NAME_MIN} e ${ORGANIZATION_NAME_MAX} caracteres.`,
      );
      return;
    }

    this.erroDoNome.set(null);
    this.enviando.set(true);

    this.service.submit(nome).subscribe({
      next: (solicitacao) => {
        const anteriores = this.solicitacoes().filter((item) => item.id !== solicitacao.id);
        this.estado.set({ tipo: 'pronto', solicitacoes: [solicitacao, ...anteriores] });
        this.acabouDeEnviar.set(true);
        this.enviando.set(false);
        this.nome.set('');

        // O formulário some; o foco vai para o acompanhamento em vez de cair no
        // início da página, o que desorientaria quem navega por teclado ou leitor.
        afterNextRender(() => this.acompanhamento()?.nativeElement.focus(), {
          injector: this.injector,
        });
      },
      error: (falha: ApiFailure) => {
        this.falhaAoEnviar.set(falha.message);
        this.enviando.set(false);
      },
    });
  }
}
