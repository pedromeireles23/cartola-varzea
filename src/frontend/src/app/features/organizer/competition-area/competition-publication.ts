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
import { RouterLink } from '@angular/router';

import { ApiFailure } from '../../../core/api/problem-details';
import { Alert, Badge, Button, Card, Dialog, Loading } from '../../../shared/ui';
import { CompetitionContext } from './competition-context';
import {
  CompetitionReadiness,
  NOT_READY_CODE,
  READINESS_LINKS,
  PublicationService,
  ReadinessItem,
} from './publication.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly checklist: CompetitionReadiness }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Checklist de prontidão e publicação (02 §9.1, 01 §7 e §9).
 *
 * Impedimentos e alertas vêm do servidor já em português; a tela só os separa e
 * oferece o caminho para resolver cada um. Quem publica é o proprietário; o auxiliar
 * acompanha a mesma lista.
 */
@Component({
  selector: 'app-competition-publication',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, DatePipe, Dialog, Loading, RouterLink],
  template: `
    <div class="pagina">
      <h1>Publicação</h1>
      <p class="intro">
        Publicar torna o campeonato visível para o público. Antes disso, o checklist confere se
        alguém conseguiria montar uma equipe válida com o que está cadastrado.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Conferindo o campeonato…" /></app-card>
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
          <app-card heading="Situação">
            <p class="situacao">
              <app-badge [tone]="publicado() ? 'success' : 'warning'">
                {{ publicado() ? 'Publicado' : 'Rascunho' }}
              </app-badge>
              @if (checklist()!.publishedAt; as publicadoEm) {
                <span>Publicado pela primeira vez em {{ publicadoEm | date: 'dd/MM/yyyy' }}</span>
              }
            </p>

            <p class="apoio">
              {{
                publicado()
                  ? 'O campeonato aparece para o público. Voltar para rascunho o esconde de novo.'
                  : 'Só a sua organização vê este campeonato.'
              }}
            </p>

            @if (checklist()!.slug; as endereco) {
              <p class="apoio">
                Endereço público:
                <a [routerLink]="['/c', endereco]">/c/{{ endereco }}</a>
                @if (!publicado()) {
                  — fora do ar enquanto o campeonato estiver em rascunho.
                }
              </p>
            }

            @if (proprietario()) {
              <div class="acoes">
                @if (publicado()) {
                  <app-button variant="secondary" (pressed)="pedirDespublicacao()">
                    Voltar para rascunho
                  </app-button>
                } @else {
                  <app-button [disabled]="!checklist()!.canPublish" (pressed)="pedirPublicacao()">
                    Publicar campeonato
                  </app-button>
                  @if (!checklist()!.canPublish) {
                    <p class="apoio">Resolva os impedimentos abaixo para liberar a publicação.</p>
                  }
                }
              </div>
            } @else {
              <p class="apoio">Somente quem é proprietário da organização publica o campeonato.</p>
            }
          </app-card>

          <app-card heading="Checklist">
            @if (impedimentos().length === 0 && alertas().length === 0) {
              <app-alert tone="success">
                Nada pendente: o catálogo comporta um elenco completo da modalidade.
              </app-alert>
            }

            @if (impedimentos().length > 0) {
              <h2 class="lista__titulo">Impedimentos</h2>
              <ul class="lista">
                @for (item of impedimentos(); track $index) {
                  <li class="lista__item lista__item--impedimento">
                    <p>{{ item.message }}</p>
                    @if (atalho(item); as link) {
                      <a [routerLink]="['..', link.rota]">{{ link.texto }}</a>
                    }
                  </li>
                }
              </ul>
            }

            @if (alertas().length > 0) {
              <h2 class="lista__titulo">Alertas</h2>
              <p class="apoio">
                Não impedem a publicação, mas deixam a disputa menos interessante.
              </p>
              <ul class="lista">
                @for (item of alertas(); track $index) {
                  <li class="lista__item lista__item--alerta">
                    <p>{{ item.message }}</p>
                    @if (atalho(item); as link) {
                      <a [routerLink]="['..', link.rota]">{{ link.texto }}</a>
                    }
                  </li>
                }
              </ul>
            }
          </app-card>
        }
      }
    </div>

    @if (proprietario()) {
      <app-dialog
        heading="Publicar o campeonato?"
        [open]="decisao() === 'publicar'"
        (dismissed)="cancelarDecisao()"
      >
        <p>
          O campeonato passa a aparecer nas buscas públicas. A modalidade fica travada a partir
          daqui, mesmo que ele volte para rascunho depois.
        </p>
        @if (falhaNaDecisao()) {
          <app-alert tone="danger">{{ falhaNaDecisao() }}</app-alert>
        }
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarDecisao()">
            Cancelar
          </app-button>
          <app-button [loading]="salvando()" (pressed)="confirmar()">Publicar</app-button>
        </div>
      </app-dialog>

      <app-dialog
        heading="Voltar para rascunho?"
        [open]="decisao() === 'despublicar'"
        (dismissed)="cancelarDecisao()"
      >
        <p>O campeonato deixa de aparecer para o público até ser publicado de novo.</p>
        @if (falhaNaDecisao()) {
          <app-alert tone="danger">{{ falhaNaDecisao() }}</app-alert>
        }
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarDecisao()">
            Cancelar
          </app-button>
          <app-button variant="danger" [loading]="salvando()" (pressed)="confirmar()">
            Voltar para rascunho
          </app-button>
        </div>
      </app-dialog>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-publication.scss'],
})
export class CompetitionPublicationPage {
  private readonly service = inject(PublicationService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly decisao = signal<'publicar' | 'despublicar' | null>(null);
  protected readonly salvando = signal(false);
  protected readonly falhaNaDecisao = signal<string | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly checklist = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.checklist : null;
  });

  protected readonly publicado = computed(() => this.checklist()?.status === 'Published');

  protected readonly impedimentos = computed(() => this.itens('Blocker'));
  protected readonly alertas = computed(() => this.itens('Warning'));

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

  protected carregar(): void {
    if (this.estado().tipo !== 'pronto') {
      this.estado.set({ tipo: 'carregando' });
    }
    this.service.readiness(this.campeonatoId).subscribe({
      next: (checklist) => this.estado.set({ tipo: 'pronto', checklist }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected atalho(item: ReadinessItem): { readonly rota: string; readonly texto: string } | null {
    return READINESS_LINKS[item.code] ?? null;
  }

  protected pedirPublicacao(): void {
    this.falhaNaDecisao.set(null);
    this.decisao.set('publicar');
  }

  protected pedirDespublicacao(): void {
    this.falhaNaDecisao.set(null);
    this.decisao.set('despublicar');
  }

  protected cancelarDecisao(): void {
    if (!this.salvando()) this.decisao.set(null);
  }

  protected confirmar(): void {
    const checklist = this.checklist();
    const decisao = this.decisao();
    if (!checklist || !decisao || this.salvando()) return;

    const publicar = decisao === 'publicar';
    this.salvando.set(true);
    this.service.setPublished(this.campeonatoId, publicar, checklist.version).subscribe({
      next: (atualizado) => {
        this.salvando.set(false);
        this.decisao.set(null);
        this.estado.set({ tipo: 'pronto', checklist: atualizado });

        // A casca mostra o status e guarda a versão da configuração: ambos mudaram.
        this.contexto.reload();
        this.avisar(
          'success',
          publicar
            ? 'Campeonato publicado. Ele já aparece para o público.'
            : 'Campeonato de volta para rascunho. Só a sua organização vê.',
        );
      },
      error: (falha: ApiFailure) => this.tratarFalha(falha),
    });
  }

  private itens(severidade: ReadinessItem['severity']): readonly ReadinessItem[] {
    return this.checklist()?.items.filter((item) => item.severity === severidade) ?? [];
  }

  private tratarFalha(falha: ApiFailure): void {
    this.salvando.set(false);
    this.decisao.set(null);
    if (falha.code === NOT_READY_CODE) {
      this.avisar('warning', 'O checklist mudou e ainda há impedimentos. Veja a lista atualizada.');
      this.carregar();
      return;
    }
    if (falha.status === 409) {
      this.avisar(
        'warning',
        'Outra pessoa decidiu sobre este campeonato enquanto você olhava. A situação foi atualizada.',
      );
      this.carregar();
      this.contexto.reload();
      return;
    }
    this.avisar('danger', falha.message);
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
