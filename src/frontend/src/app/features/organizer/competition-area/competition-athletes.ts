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
import { NgTemplateOutlet } from '@angular/common';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../../core/api/problem-details';
import {
  Alert,
  Badge,
  Button,
  Card,
  Dialog,
  FormField,
  Loading,
  SelectField,
  SelectOption,
} from '../../../shared/ui';
import {
  ATHLETE_DUPLICATE_CODE,
  ATHLETE_MAX_PRICE,
  ATHLETE_MIN_PRICE,
  ATHLETE_NAME_MAX,
  ATHLETE_NAME_MIN,
  ATHLETE_POSITION_LOCKED_CODE,
  ATHLETE_TRANSFER_CODE,
  Athlete,
  AthletePosition,
  AthleteService,
  PriceTier,
} from './athlete.service';
import { registrationWindowText } from '../competition.service';
import { CompetitionContext } from './competition-context';
import { RealTeam, RealTeamService } from './real-team.service';

type Estado =
  | { readonly tipo: 'carregando' }
  | {
      readonly tipo: 'pronto';
      readonly athletes: readonly Athlete[];
      readonly teams: readonly RealTeam[];
    }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

interface AthleteFormValue {
  readonly sportingName: string;
  readonly position: AthletePosition;
  readonly realTeamId: string;
  readonly priceTier: PriceTier;
  readonly initialPriceOverride: number | null;
}

const POSITION_OPTIONS: readonly SelectOption[] = [
  { value: 'Goalkeeper', label: 'Goleiro' },
  { value: 'Defender', label: 'Defensor' },
  { value: 'Midfielder', label: 'Meio-campista' },
  { value: 'Forward', label: 'Atacante' },
];

const PRICE_TIER_OPTIONS: readonly SelectOption[] = [
  { value: 'Star', label: 'Destaque' },
  { value: 'Regular', label: 'Regular' },
  { value: 'Basic', label: 'Básico' },
];

const POSITION_LABELS: Readonly<Record<AthletePosition, string>> = {
  Goalkeeper: 'Goleiro',
  Defender: 'Defensor',
  Midfielder: 'Meio-campista',
  Forward: 'Atacante',
};

const PRICE_TIER_LABELS: Readonly<Record<PriceTier, string>> = {
  Star: 'Destaque',
  Regular: 'Regular',
  Basic: 'Básico',
};

/** Catálogo manual de atletas, inscrição, preço e disponibilidade (01 §7/§9). */
@Component({
  selector: 'app-competition-athletes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Dialog, FormField, Loading, NgTemplateOutlet, SelectField],
  template: `
    <div class="pagina">
      <h1>Atletas</h1>
      <p class="intro">
        Inscreva cada atleta em um time e defina posição e nível de preço. Um desligamento preserva
        o histórico e deixa o atleta indisponível para novas compras.
      </p>

      @if (inscricaoEncerrada(); as texto) {
        <app-alert tone="warning">
          Inscrições {{ texto }} Atletas já inscritos continuam editáveis; para inscrever novos,
          estenda o prazo na configuração.
        </app-alert>
      }

      <div class="foco" tabindex="-1" #aviso>
        @if (retorno(); as resultado) {
          <app-alert [tone]="resultado.tom">{{ resultado.texto }}</app-alert>
        }
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-card><app-loading label="Buscando os atletas…" /></app-card>
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
          @if (proprietario() && editandoNovo()) {
            <app-card heading="Novo atleta">
              <ng-container *ngTemplateOutlet="athleteForm" />
            </app-card>
          } @else if (proprietario()) {
            <div class="acoes">
              <app-button
                [disabled]="timesAtivos().length === 0 || inscricaoEncerrada() !== null"
                (pressed)="abrirNovo()"
              >
                Adicionar atleta
              </app-button>
            </div>
            @if (timesAtivos().length === 0) {
              <app-alert tone="warning">
                Cadastre ao menos um time ativo antes de adicionar atletas.
              </app-alert>
            }
          }

          <div class="athletes" role="list">
            @for (athlete of athletes(); track athlete.id) {
              <app-card>
                <article
                  class="athlete"
                  role="listitem"
                  [class.athlete--released]="athlete.status === 'Released'"
                >
                  <div class="avatar" aria-hidden="true">{{ iniciais(athlete.sportingName) }}</div>
                  <div class="athlete__content">
                    @if (editando(athlete.id)) {
                      <ng-container *ngTemplateOutlet="athleteForm" />
                    } @else {
                      <div class="athlete__header">
                        <h2>{{ athlete.sportingName }}</h2>
                        @if (athlete.status === 'Released') {
                          <app-badge tone="neutral">Desligado</app-badge>
                        } @else if (athlete.isEliminated) {
                          <app-badge tone="neutral">Time eliminado</app-badge>
                        } @else if (!athlete.isAvailable) {
                          <app-badge tone="warning">Indisponível</app-badge>
                        } @else {
                          <app-badge tone="success">Disponível</app-badge>
                        }
                      </div>
                      <p class="athlete__meta">
                        <span>{{ athlete.realTeamName }}</span>
                        <span>·</span>
                        <span>{{ positionLabel(athlete.position) }}</span>
                        <span>·</span>
                        <span>{{ tierLabel(athlete.priceTier) }}</span>
                        <span>·</span>
                        <span>{{ priceLabel(athlete.initialPrice) }}</span>
                      </p>
                      @if (proprietario() && athlete.status === 'Active') {
                        <div class="acoes">
                          <app-button variant="secondary" (pressed)="abrirEdicao(athlete)">
                            Editar<span class="sr-only"> {{ athlete.sportingName }}</span>
                          </app-button>
                          <app-button variant="ghost" (pressed)="pedirDesligamento(athlete)">
                            Desligar<span class="sr-only"> {{ athlete.sportingName }}</span>
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
                  {{
                    proprietario()
                      ? 'Nenhum atleta cadastrado. Adicione as inscrições dos times.'
                      : 'Nenhum atleta cadastrado ainda.'
                  }}
                </p>
              </app-card>
            }
          </div>
        }
      }
    </div>

    <ng-template #athleteForm>
      <form class="athlete-form" (submit)="salvar($event)" novalidate>
        <app-form-field
          label="Nome esportivo"
          placeholder="Ex.: Bia"
          [required]="true"
          [maxLength]="nomeMax"
          [error]="erroNome()"
          [(value)]="nome"
        />
        <div class="form-grid">
          <app-select-field
            label="Time"
            [options]="opcoesTime()"
            [disabled]="editandoId() !== null"
            [error]="erroTime()"
            hint="O time não pode ser trocado depois da inscrição."
            [(value)]="timeId"
          />
          <app-select-field
            label="Posição"
            [options]="positionOptions"
            [error]="erroPosicao()"
            [(value)]="posicao"
          />
          <app-select-field
            label="Nível de preço"
            [options]="priceTierOptions"
            hint="O nível ajusta o valor de referência da posição."
            [(value)]="nivel"
          />
          <app-form-field
            label="Preço exato (opcional)"
            type="number"
            placeholder="Ex.: 8,50"
            hint="Deixe vazio para usar o preço calculado pelo nível."
            [error]="erroPreco()"
            [(value)]="precoExato"
          />
        </div>
        <div class="acoes">
          <app-button type="submit" [loading]="salvando()">
            {{ editandoNovo() ? 'Adicionar atleta' : 'Salvar' }}
          </app-button>
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarEdicao()">
            Cancelar
          </app-button>
        </div>
      </form>
    </ng-template>

    @if (proprietario()) {
      <app-dialog
        [heading]="'Desligar ' + (desligando()?.sportingName ?? '') + '?'"
        [open]="desligando() !== null"
        (dismissed)="cancelarDesligamento()"
      >
        <p>
          O atleta ficará indisponível para novas compras. Time, preço e histórico serão
          preservados.
        </p>
        @if (falhaAoDesligar()) {
          <app-alert tone="danger">{{ falhaAoDesligar() }}</app-alert>
        }
        <div dialogActions class="dialogo__acoes">
          <app-button variant="ghost" [disabled]="salvando()" (pressed)="cancelarDesligamento()">
            Cancelar
          </app-button>
          <app-button variant="danger" [loading]="salvando()" (pressed)="desligar()">
            Desligar atleta
          </app-button>
        </div>
      </app-dialog>
    }
  `,
  styleUrls: ['../organizer.scss', './competition-pages.scss', './competition-athletes.scss'],
})
export class CompetitionAthletesPage {
  private readonly athleteService = inject(AthleteService);
  private readonly teamService = inject(RealTeamService);
  private readonly injector = inject(Injector);
  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly contexto = inject(CompetitionContext);
  protected readonly proprietario = this.contexto.proprietario;

  /** Frase do prazo quando ele já passou; nulo com as inscrições abertas. */
  protected readonly inscricaoEncerrada = computed(() => {
    const janela = this.contexto.campeonato()?.registrationWindow;
    return janela && !janela.isOpen ? registrationWindowText(janela).toLowerCase() : null;
  });
  protected readonly nomeMax = ATHLETE_NAME_MAX;
  protected readonly positionOptions = POSITION_OPTIONS;
  protected readonly priceTierOptions = PRICE_TIER_OPTIONS;
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly editandoNovo = signal(false);
  protected readonly editandoId = signal<string | null>(null);
  protected readonly nome = signal('');
  protected readonly timeId = signal('');
  protected readonly posicao = signal<AthletePosition>('Forward');
  protected readonly nivel = signal<PriceTier>('Regular');
  protected readonly precoExato = signal('');
  protected readonly erroNome = signal<string | undefined>(undefined);
  protected readonly erroTime = signal<string | undefined>(undefined);
  protected readonly erroPosicao = signal<string | undefined>(undefined);
  protected readonly erroPreco = signal<string | undefined>(undefined);
  protected readonly salvando = signal(false);
  protected readonly desligando = signal<Athlete | null>(null);
  protected readonly falhaAoDesligar = signal<string | null>(null);
  protected readonly retorno = signal<{
    readonly tom: 'success' | 'warning' | 'danger';
    readonly texto: string;
  } | null>(null);

  protected readonly athletes = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.athletes : [];
  });

  protected readonly teams = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.teams : [];
  });

  protected readonly timesAtivos = computed(() => this.teams().filter((team) => !team.isArchived));

  protected readonly opcoesTime = computed<readonly SelectOption[]>(() => {
    const currentTeam = this.athletes().find(
      (athlete) => athlete.id === this.editandoId(),
    )?.realTeamId;
    return this.teams()
      .filter((team) => !team.isArchived || team.id === currentTeam)
      .map((team) => ({
        value: team.id,
        label: `${team.name}${team.isArchived ? ' (arquivado)' : ''}`,
      }));
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
    forkJoin({
      athletes: this.athleteService.list(this.campeonatoId),
      teams: this.teamService.list(this.campeonatoId),
    }).subscribe({
      next: ({ athletes, teams }) => this.estado.set({ tipo: 'pronto', athletes, teams }),
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected abrirNovo(): void {
    const firstTeam = this.timesAtivos()[0];
    if (!firstTeam) return;
    this.retorno.set(null);
    this.editandoId.set(null);
    this.editandoNovo.set(true);
    this.preencherFormulario('', firstTeam.id, 'Forward', 'Regular', null);
  }

  protected abrirEdicao(athlete: Athlete): void {
    this.retorno.set(null);
    this.editandoNovo.set(false);
    this.editandoId.set(athlete.id);
    this.preencherFormulario(
      athlete.sportingName,
      athlete.realTeamId,
      athlete.position,
      athlete.priceTier,
      athlete.initialPriceOverride,
    );
  }

  protected editando(id: string): boolean {
    return this.editandoId() === id;
  }

  protected cancelarEdicao(): void {
    this.editandoNovo.set(false);
    this.editandoId.set(null);
    this.limparErros();
  }

  protected salvar(event: Event): void {
    event.preventDefault();
    const input = this.validarFormulario();
    if (!input) return;

    this.salvando.set(true);
    const athlete = this.athletes().find((item) => item.id === this.editandoId());
    const request = athlete
      ? this.athleteService.update(this.campeonatoId, athlete.id, input, athlete.version)
      : this.athleteService.create(this.campeonatoId, input);
    request.subscribe({
      next: (saved) =>
        this.concluir(
          athlete ? `${saved.sportingName} salvo.` : `${saved.sportingName} adicionado.`,
        ),
      error: (failure: ApiFailure) => this.tratarFalha(failure),
    });
  }

  protected pedirDesligamento(athlete: Athlete): void {
    this.falhaAoDesligar.set(null);
    this.desligando.set(athlete);
  }

  protected cancelarDesligamento(): void {
    if (!this.salvando()) this.desligando.set(null);
  }

  protected desligar(): void {
    const athlete = this.desligando();
    if (!athlete || this.salvando()) return;

    this.salvando.set(true);
    this.athleteService.release(this.campeonatoId, athlete.id).subscribe({
      next: () => {
        this.salvando.set(false);
        this.desligando.set(null);
        this.concluir(`${athlete.sportingName} desligado.`);
      },
      error: (failure: ApiFailure) => {
        this.salvando.set(false);
        if (failure.status === 404) {
          this.desligando.set(null);
          this.concluir(`${athlete.sportingName} já estava desligado. A lista foi atualizada.`);
          return;
        }
        this.falhaAoDesligar.set(failure.message);
      },
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

  protected positionLabel(position: AthletePosition): string {
    return POSITION_LABELS[position];
  }

  protected tierLabel(tier: PriceTier): string {
    return PRICE_TIER_LABELS[tier];
  }

  protected priceLabel(price: number): string {
    return `${price.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} créditos`;
  }

  private preencherFormulario(
    name: string,
    teamId: string,
    position: AthletePosition,
    tier: PriceTier,
    exactPrice: number | null,
  ): void {
    this.nome.set(name);
    this.timeId.set(teamId);
    this.posicao.set(position);
    this.nivel.set(tier);
    this.precoExato.set(exactPrice?.toString() ?? '');
    this.limparErros();
  }

  private validarFormulario(): AthleteFormValue | null {
    this.limparErros();
    const sportingName = this.nome().trim();
    if (sportingName.length < ATHLETE_NAME_MIN || sportingName.length > ATHLETE_NAME_MAX) {
      this.erroNome.set(`Use de ${ATHLETE_NAME_MIN} a ${ATHLETE_NAME_MAX} caracteres.`);
    }

    const currentTeam = this.teams().find((team) => team.id === this.timeId());
    if (!currentTeam || (!this.editandoId() && currentTeam.isArchived)) {
      this.erroTime.set('Escolha um time ativo.');
    }

    const rawPrice = this.precoExato().trim().replace(',', '.');
    const exactPrice = rawPrice === '' ? null : Number(rawPrice);
    if (
      exactPrice !== null &&
      (!Number.isFinite(exactPrice) ||
        exactPrice < ATHLETE_MIN_PRICE ||
        exactPrice > ATHLETE_MAX_PRICE ||
        Math.round(exactPrice * 100) !== exactPrice * 100)
    ) {
      this.erroPreco.set(
        `Use um valor entre ${ATHLETE_MIN_PRICE},00 e ${ATHLETE_MAX_PRICE},00, com até duas casas decimais.`,
      );
    }

    if (this.erroNome() || this.erroTime() || this.erroPreco()) return null;
    return {
      sportingName,
      position: this.posicao(),
      realTeamId: this.timeId(),
      priceTier: this.nivel(),
      initialPriceOverride: exactPrice,
    };
  }

  private limparErros(): void {
    this.erroNome.set(undefined);
    this.erroTime.set(undefined);
    this.erroPosicao.set(undefined);
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
    if (failure.code === ATHLETE_DUPLICATE_CODE) {
      this.erroNome.set('Já existe um atleta com esse nome esportivo neste campeonato.');
      return;
    }
    if (failure.code === ATHLETE_POSITION_LOCKED_CODE) {
      this.erroPosicao.set('A posição não pode mudar depois que o atleta entra no mercado.');
      return;
    }
    if (failure.code === ATHLETE_TRANSFER_CODE) {
      this.erroTime.set('O time do atleta não pode ser alterado.');
      return;
    }
    if (failure.status === 409) {
      this.cancelarEdicao();
      this.avisar(
        'warning',
        'Este atleta foi alterado por outra pessoa. A lista foi atualizada; refaça sua mudança.',
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
