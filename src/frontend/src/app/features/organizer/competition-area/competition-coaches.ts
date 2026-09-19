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
import {
  Alert,
  Badge,
  Button,
  Card,
  FormField,
  Loading,
  SelectField,
  SelectOption,
} from '../../../shared/ui';
import { PriceTier } from './athlete.service';
import {
  COACH_MAX_PRICE,
  COACH_MIN_PRICE,
  COACH_NAME_MAX,
  COACH_NAME_MIN,
  COACH_PRICE_LOCKED_CODE,
  Coach,
  CoachInput,
  CoachService,
} from './coach.service';
import { CompetitionContext } from './competition-context';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly coaches: readonly Coach[] }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

const PRICE_TIER_OPTIONS: readonly SelectOption[] = [
  { value: 'Star', label: 'Destaque' },
  { value: 'Regular', label: 'Regular' },
  { value: 'Basic', label: 'Básico' },
];

const PRICE_TIER_LABELS: Readonly<Record<PriceTier, string>> = {
  Star: 'Destaque',
  Regular: 'Regular',
  Basic: 'Básico',
};

/** Técnico único e estável de cada time, com nome pessoal opcional. */
@Component({
  selector: 'app-competition-coaches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, FormField, Loading, SelectField],
  template: `
    <div class="pagina">
      <h1>Técnicos</h1>
      <p class="intro">
        Cada time recebe um técnico automaticamente. Informe o nome da pessoa quando houver uma
        representante; caso contrário, o fantasy usa “Técnico do nome do time”.
      </p>

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando os técnicos…" /></app-card>
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
          <div class="coaches" role="list">
            @for (coach of coaches(); track coach.id) {
              <app-card>
                <article
                  class="coach"
                  role="listitem"
                  [class.coach--unavailable]="!coach.isAvailable"
                >
                  <div class="avatar" aria-hidden="true">{{ iniciais(coach.effectiveName) }}</div>
                  <div class="coach__content">
                    @if (editando(coach.id)) {
                      <form class="coach-form" (submit)="salvar($event, coach)" novalidate>
                        <app-form-field
                          label="Nome da pessoa (opcional)"
                          placeholder="Ex.: Ana Lima"
                          hint="Deixe vazio para usar o nome automático do time."
                          [maxLength]="nomeMax"
                          [error]="erroNome()"
                          [(value)]="nome"
                        />
                        <div class="form-grid">
                          <app-select-field
                            label="Nível de preço"
                            [options]="priceTierOptions"
                            [disabled]="coach.isMarketLocked"
                            [hint]="
                              coach.isMarketLocked
                                ? 'Travado desde a entrada no mercado: alguém pode tê-lo comprado por esse valor.'
                                : undefined
                            "
                            [(value)]="nivel"
                          />
                          <app-form-field
                            label="Preço exato (opcional)"
                            type="number"
                            placeholder="Ex.: 8,50"
                            [disabled]="coach.isMarketLocked"
                            [hint]="
                              coach.isMarketLocked
                                ? 'Travado junto com o nível.'
                                : 'Deixe vazio para usar o preço calculado pelo nível.'
                            "
                            [error]="erroPreco()"
                            [(value)]="precoExato"
                          />
                        </div>
                        <div class="acoes">
                          <app-button type="submit" [loading]="salvando()">Salvar</app-button>
                          <app-button
                            variant="ghost"
                            [disabled]="salvando()"
                            (pressed)="cancelarEdicao()"
                          >
                            Cancelar
                          </app-button>
                        </div>
                      </form>
                    } @else {
                      <div class="coach__header">
                        <h2>{{ coach.effectiveName }}</h2>
                        @if (coach.isAvailable) {
                          <app-badge tone="success">Disponível</app-badge>
                        } @else if (coach.isEliminated) {
                          <app-badge tone="neutral">Time eliminado</app-badge>
                        } @else {
                          <app-badge tone="warning">Indisponível</app-badge>
                        }
                      </div>
                      <p class="coach__meta">
                        <span>{{ coach.realTeamName }}</span>
                        <span>·</span>
                        <span>{{ tierLabel(coach.priceTier) }}</span>
                        <span>·</span>
                        <span>{{ priceLabel(coach.initialPrice) }}</span>
                      </p>
                      @if (!coach.displayName) {
                        <p class="apoio">Nome automático — nenhuma pessoa foi informada.</p>
                      }
                      @if (proprietario() && coach.isAvailable) {
                        <div class="acoes">
                          <app-button variant="secondary" (pressed)="abrirEdicao(coach)">
                            Editar<span class="sr-only"> {{ coach.effectiveName }}</span>
                          </app-button>
                        </div>
                      }
                    }
                  </div>
                </article>
              </app-card>
            } @empty {
              <app-card>
                <p class="apoio">
                  Cadastre um time para que seu técnico seja criado automaticamente.
                </p>
              </app-card>
            }
          </div>
        }
      }
    </div>
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-coaches.scss'],
})
export class CompetitionCoachesPage {
  private readonly coachService = inject(CoachService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;
  protected readonly nomeMax = COACH_NAME_MAX;
  protected readonly priceTierOptions = PRICE_TIER_OPTIONS;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly editandoId = signal<string | null>(null);
  protected readonly nome = signal('');
  protected readonly nivel = signal<PriceTier>('Regular');
  protected readonly precoExato = signal('');
  protected readonly erroNome = signal<string | undefined>(undefined);
  protected readonly erroPreco = signal<string | undefined>(undefined);
  protected readonly salvando = signal(false);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly coaches = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.coaches : [];
  });

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
    if (this.estado().tipo !== 'pronto') this.estado.set({ tipo: 'carregando' });
    this.coachService.list(this.campeonatoId).subscribe({
      next: (coaches) => this.estado.set({ tipo: 'pronto', coaches }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrirEdicao(coach: Coach): void {
    this.retorno.set(null);
    this.editandoId.set(coach.id);
    this.nome.set(coach.displayName ?? '');
    this.nivel.set(coach.priceTier);
    this.precoExato.set(coach.initialPriceOverride?.toString() ?? '');
    this.limparErros();
  }

  protected editando(id: string): boolean {
    return this.editandoId() === id;
  }

  protected cancelarEdicao(): void {
    this.editandoId.set(null);
    this.limparErros();
  }

  protected salvar(event: Event, coach: Coach): void {
    event.preventDefault();
    const input = this.validarFormulario();
    if (!input) return;

    this.salvando.set(true);
    this.coachService.update(this.campeonatoId, coach.id, input, coach.version).subscribe({
      next: (saved) => this.concluir(`${saved.effectiveName} salvo.`),
      error: (failure: ApiFailure) => this.tratarFalha(failure),
    });
  }

  protected iniciais(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0])
      .join('')
      .toLocaleUpperCase('pt-BR');
  }

  protected tierLabel(tier: PriceTier): string {
    return PRICE_TIER_LABELS[tier];
  }

  protected priceLabel(price: number): string {
    return `${price.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} créditos`;
  }

  private validarFormulario(): CoachInput | null {
    this.limparErros();
    const displayName = this.nome().trim() || null;
    if (
      displayName !== null &&
      (displayName.length < COACH_NAME_MIN || displayName.length > COACH_NAME_MAX)
    ) {
      this.erroNome.set(
        `Quando informado, use de ${COACH_NAME_MIN} a ${COACH_NAME_MAX} caracteres.`,
      );
    }

    const rawPrice = this.precoExato().trim().replace(',', '.');
    const exactPrice = rawPrice === '' ? null : Number(rawPrice);
    if (
      exactPrice !== null &&
      (!Number.isFinite(exactPrice) ||
        exactPrice < COACH_MIN_PRICE ||
        exactPrice > COACH_MAX_PRICE ||
        Math.round(exactPrice * 100) !== exactPrice * 100)
    ) {
      this.erroPreco.set(
        `Use um valor entre ${COACH_MIN_PRICE},00 e ${COACH_MAX_PRICE},00, com até duas casas decimais.`,
      );
    }

    if (this.erroNome() || this.erroPreco()) return null;
    return {
      displayName,
      priceTier: this.nivel(),
      initialPriceOverride: exactPrice,
    };
  }

  private limparErros(): void {
    this.erroNome.set(undefined);
    this.erroPreco.set(undefined);
  }

  private concluir(texto: string): void {
    this.salvando.set(false);
    this.cancelarEdicao();
    this.avisar('success', texto);
    this.carregar();
  }

  private tratarFalha(failure: ApiFailure): void {
    this.salvando.set(false);
    if (failure.code === COACH_PRICE_LOCKED_CODE) {
      this.erroPreco.set(
        'O nível e o preço não podem mudar depois que o técnico entra no mercado.',
      );
      return;
    }
    if (failure.status === 409) {
      this.cancelarEdicao();
      this.avisar(
        'warning',
        'Este técnico foi alterado por outra pessoa. A lista foi atualizada; refaça sua mudança.',
      );
      this.carregar();
      return;
    }
    this.avisar('danger', failure.message);
  }

  private avisar(tom: 'success' | 'warning' | 'danger', texto: string): void {
    this.retorno.set({ tom, texto });
    afterNextRender(() => this.aviso()?.nativeElement.focus(), { injector: this.injector });
  }
}
