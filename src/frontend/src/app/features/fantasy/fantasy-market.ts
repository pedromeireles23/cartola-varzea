import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import {
  Alert,
  Badge,
  Button,
  Card,
  FormField,
  Loading,
  SelectField,
  SelectOption,
} from '../../shared/ui';
import { AthletePosition } from '../organizer/competition-area/athlete.service';
import {
  POSITION_LABELS,
  POSITION_ORDER,
  assetRoleLabel,
  credits,
  fantasyRefusalText,
  teamInitials,
} from './fantasy-format';
import {
  FANTASY_CONFLICT_CODE,
  FANTASY_MARKET_CLOSED_CODE,
  FantasyMarket,
  FantasyOverview,
  FantasyService,
  MarketItem,
} from './fantasy.service';
import { FantasyNotice } from './fantasy-notice';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview; readonly mercado: FantasyMarket }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

type Posicao = AthletePosition | 'Coach' | '';
type Situacao = 'todos' | 'compraveis' | 'elenco';
type Ordem = 'nome' | 'menor' | 'maior';

interface Grupo {
  readonly timeId: string;
  readonly time: string;
  readonly itens: readonly MarketItem[];
}

/** Times por página: o mercado de várzea costuma ter de 6 a 16 times. */
const TIMES_POR_PAGINA = 6;

/** `?posicao=` em português, como as rotas; é o que o campo de escalação vai mandar. */
const POSICAO_NA_URL: Readonly<Record<string, Posicao>> = {
  goleiro: 'Goalkeeper',
  defensor: 'Defender',
  'meio-campista': 'Midfielder',
  atacante: 'Forward',
  tecnico: 'Coach',
};

/** Bloqueios que valem para o mercado inteiro: são avisados uma vez, não em cada item. */
const BLOQUEIOS_GERAIS = new Set(['not_joined', 'market_closed']);

/**
 * Mercado do participante (02 §9.1, `/c/:campeonato/mercado`).
 *
 * Agrupado por time real, que é como se procura atleta na várzea: primeiro o time,
 * depois a pessoa (02 §8). Busca, posição, situação e ordenação atuam dentro do
 * agrupamento. O motivo de um item não poder ser comprado vem do servidor e aparece no
 * próprio item; o servidor continua recusando a compra mesmo se a tela for ignorada.
 */
@Component({
  selector: 'app-fantasy-market',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, FormField, Loading, MarketClock, RouterLink, SelectField],
  template: `
    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Mercado</h1>
        <app-card><app-loading label="Abrindo o mercado…" /></app-card>
      }
      @case ('erro') {
        <h1>Mercado</h1>
        <app-card>
          @if (falha()!.status === 404) {
            <app-alert tone="warning">
              Não encontramos este campeonato. Ele pode ter saído do ar ou o endereço estar errado.
            </app-alert>
            <a class="acao" routerLink="/campeonatos">Ver campeonatos publicados</a>
          } @else {
            <app-alert tone="danger">{{ falha()!.message }}</app-alert>
            <app-button variant="secondary" (pressed)="carregar()">Tentar de novo</app-button>
          }
        </app-card>
      }
      @case ('pronto') {
        <h1>Mercado</h1>
        <p class="intro">{{ visao()!.competitionName }}</p>

        <app-card>
          <app-market-clock [market]="mercado()!.market" (closed)="carregar()" />
        </app-card>

        @if (daEscalacao() && podeOperar()) {
          <app-alert tone="info">
            Escolhendo {{ vagaEscolhida() }} para a escalação. Depois da compra você volta para o
            campo.
            <a [routerLink]="['/c', campeonato(), 'escalacao']">Voltar para a escalação</a>
          </app-alert>
        }

        @if (visao()!.entry; as entrada) {
          <section class="resumo" aria-label="Seu elenco no mercado">
            <p class="resumo__item">
              <span class="resumo__rotulo">Saldo</span>
              <strong>{{ creditos(entrada.balance) }}</strong>
            </p>
            <p class="resumo__item">
              <span class="resumo__rotulo">Atletas</span>
              <strong>{{ atletasNoElenco() }} de {{ visao()!.profile.squadAthletes }}</strong>
            </p>
            <p class="resumo__item">
              <span class="resumo__rotulo">Técnico</span>
              <strong>{{ temTecnico() ? '1 de 1' : '0 de 1' }}</strong>
            </p>
            <p class="resumo__item">
              <span class="resumo__rotulo">Por time</span>
              <strong
                >até {{ visao()!.teamLimit.maxAthletes }} ({{
                  visao()!.teamLimit.maxStarters
                }}
                titulares)</strong
              >
            </p>
          </section>
        } @else {
          <app-alert tone="info">
            Você ainda não entrou neste campeonato. Dá para olhar o mercado, mas para comprar é
            preciso <a [routerLink]="['/c', campeonato(), 'jogar']">entrar no campeonato</a>.
          </app-alert>
        }

        <p class="sr-only" role="status" aria-live="polite">{{ anuncio() }}</p>
        @if (falhaNaOperacao(); as mensagem) {
          <app-alert tone="danger">{{ mensagem }}</app-alert>
        }

        <app-card heading="Filtros">
          <div class="filtros">
            <app-form-field label="Buscar por nome" placeholder="Ex.: Bia" [(value)]="busca" />
            <app-select-field label="Posição" [options]="opcoesPosicao" [(value)]="filtroPosicao" />
            <app-select-field label="Situação" [options]="opcoesSituacao" [(value)]="situacao" />
            <app-select-field label="Ordenar" [options]="opcoesOrdem" [(value)]="ordem" />
          </div>
          <p class="apoio">{{ contagem() }}</p>
        </app-card>

        @for (grupo of gruposVisiveis(); track grupo.timeId) {
          <app-card>
            <section class="grupo" [attr.aria-labelledby]="'time-' + grupo.timeId">
              <header class="grupo__topo">
                <div class="escudo" aria-hidden="true">{{ iniciais(grupo.time) }}</div>
                <div>
                  <h2 class="grupo__nome" [id]="'time-' + grupo.timeId">{{ grupo.time }}</h2>
                  @if (visao()!.entry) {
                    <p class="apoio">
                      No seu elenco: {{ doTime(grupo.timeId) }} de
                      {{ visao()!.teamLimit.maxAthletes }} atletas
                    </p>
                  }
                </div>
              </header>

              <ul class="itens">
                @for (item of grupo.itens; track item.id) {
                  <li class="item" [class.item--indisponivel]="!item.isAvailable && !item.isOwned">
                    <div class="item__dados">
                      <p class="item__nome">{{ item.name }}</p>
                      <p class="item__detalhe">{{ papel(item) }} · {{ creditos(item.price) }}</p>
                      @if (item.isOwned) {
                        <app-badge tone="brand">No seu elenco</app-badge>
                      } @else if (!item.isAvailable) {
                        <app-badge tone="neutral">Indisponível</app-badge>
                      }
                      @if (motivo(item); as texto) {
                        <p class="item__motivo">{{ texto }}</p>
                      }
                    </div>
                    @if (podeOperar()) {
                      @if (item.isOwned) {
                        <app-button
                          variant="secondary"
                          [loading]="operando() === item.id"
                          [disabled]="operando() !== null"
                          (pressed)="vender(item)"
                        >
                          Vender<span class="sr-only"> {{ item.name }}</span>
                        </app-button>
                      } @else {
                        <app-button
                          [loading]="operando() === item.id"
                          [disabled]="item.blockCode !== null || operando() !== null"
                          (pressed)="comprar(item)"
                        >
                          Comprar<span class="sr-only"> {{ item.name }}</span>
                        </app-button>
                      }
                    }
                  </li>
                }
              </ul>
            </section>
          </app-card>
        } @empty {
          <app-card>
            <p class="apoio">Nenhum atleta ou técnico corresponde a esses filtros.</p>
          </app-card>
        }

        @if (timesRestantes() > 0) {
          <app-button variant="secondary" (pressed)="mostrarMais()">
            Mostrar mais times ({{ timesRestantes() }})
          </app-button>
        }
      }
    }
  `,
  styleUrl: './fantasy.scss',
})
export class FantasyMarketPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Filtro inicial vindo do campo de escalação: `?posicao=goleiro`. */
  readonly posicao = input<string>();

  /** `?origem=escalacao`: a vaga do campo abriu o mercado, e a compra volta para lá. */
  readonly origem = input<string>();

  private readonly router = inject(Router);
  private readonly notice = inject(FantasyNotice);

  protected readonly opcoesPosicao: readonly SelectOption[] = [
    { value: '', label: 'Todas' },
    ...POSITION_ORDER.map((position) => ({ value: position, label: POSITION_LABELS[position] })),
    { value: 'Coach', label: 'Técnico' },
  ];

  protected readonly opcoesSituacao: readonly SelectOption[] = [
    { value: 'todos', label: 'Todos' },
    { value: 'compraveis', label: 'Posso comprar' },
    { value: 'elenco', label: 'No meu elenco' },
  ];

  protected readonly opcoesOrdem: readonly SelectOption[] = [
    { value: 'nome', label: 'Posição e nome' },
    { value: 'menor', label: 'Menor preço' },
    { value: 'maior', label: 'Maior preço' },
  ];

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly busca = signal('');
  protected readonly filtroPosicao = linkedSignal<string>(
    () => POSICAO_NA_URL[this.posicao() ?? ''] ?? '',
  );
  protected readonly situacao = signal<string>('todos');
  protected readonly ordem = signal<string>('nome');
  protected readonly operando = signal<string | null>(null);
  protected readonly anuncio = signal('');
  protected readonly falhaNaOperacao = signal<string | null>(null);

  protected readonly visao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.visao : null;
  });

  protected readonly mercado = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.mercado : null;
  });

  protected readonly daEscalacao = computed(() => this.origem() === 'escalacao');

  /** "um goleiro", "o técnico": o que a vaga pediu, no aviso do topo. */
  protected readonly vagaEscolhida = computed(() => {
    const posicao = POSICAO_NA_URL[this.posicao() ?? ''];
    if (!posicao) {
      return 'um atleta';
    }
    return posicao === 'Coach'
      ? 'o técnico'
      : `um ${POSITION_LABELS[posicao].toLocaleLowerCase('pt-BR')}`;
  });

  protected readonly podeOperar = computed(
    () => this.visao()?.entry !== null && this.mercado()?.market.isOpen === true,
  );

  protected readonly atletasNoElenco = computed(
    () => this.visao()?.entry?.slots.filter((slot) => slot.kind === 'Athlete').length ?? 0,
  );

  protected readonly temTecnico = computed(
    () => this.visao()?.entry?.slots.some((slot) => slot.kind === 'Coach') ?? false,
  );

  private readonly filtrados = computed(() => {
    const termo = normalizar(this.busca().trim());
    const posicao = this.filtroPosicao() as Posicao;
    const situacao = this.situacao() as Situacao;
    return (this.mercado()?.items ?? []).filter(
      (item) =>
        (termo === '' || normalizar(item.name).includes(termo)) &&
        (posicao === '' ||
          (posicao === 'Coach' ? item.kind === 'Coach' : item.position === posicao)) &&
        (situacao === 'todos' ||
          (situacao === 'elenco' ? item.isOwned : !item.isOwned && item.blockCode === null)),
    );
  });

  protected readonly grupos = computed<readonly Grupo[]>(() => {
    const ordem = this.ordem() as Ordem;
    const porTime = new Map<string, MarketItem[]>();
    for (const item of this.filtrados()) {
      porTime.set(item.realTeamId, [...(porTime.get(item.realTeamId) ?? []), item]);
    }
    return [...porTime.entries()]
      .map(([timeId, itens]) => ({
        timeId,
        time: itens[0]!.realTeamName,
        itens: [...itens].sort((a, b) => comparar(a, b, ordem)),
      }))
      .sort((a, b) => a.time.localeCompare(b.time, 'pt-BR'));
  });

  private readonly filtro = computed(() =>
    [this.busca(), this.filtroPosicao(), this.situacao(), this.ordem()].join('|'),
  );

  /**
   * Volta à primeira página quando o filtro muda. Reler o mercado depois de uma compra
   * não muda o filtro, então a pessoa continua onde estava.
   */
  private readonly limite = linkedSignal({
    source: this.filtro,
    computation: () => TIMES_POR_PAGINA,
  });

  protected readonly gruposVisiveis = computed(() => this.grupos().slice(0, this.limite()));

  protected readonly timesRestantes = computed(() =>
    Math.max(0, this.grupos().length - this.limite()),
  );

  protected readonly contagem = computed(() => {
    const itens = this.filtrados().length;
    const times = this.grupos().length;
    return `${itens} ${itens === 1 ? 'opção' : 'opções'} em ${times} ${times === 1 ? 'time' : 'times'}`;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  protected papel(item: MarketItem): string {
    return assetRoleLabel(item.kind, item.position);
  }

  protected iniciais(nome: string): string {
    return teamInitials(nome);
  }

  protected doTime(timeId: string): number {
    return (
      this.visao()?.entry?.slots.filter(
        (slot) => slot.kind === 'Athlete' && slot.realTeamId === timeId,
      ).length ?? 0
    );
  }

  /** O motivo do bloqueio só aparece no item quando é dele, não do mercado inteiro. */
  protected motivo(item: MarketItem): string | null {
    return item.isOwned || item.blockCode === null || BLOQUEIOS_GERAIS.has(item.blockCode)
      ? null
      : item.blockReason;
  }

  protected mostrarMais(): void {
    this.limite.update((atual) => atual + TIMES_POR_PAGINA);
  }

  protected carregar(): void {
    forkJoin({
      visao: this.service.overview(this.campeonato()),
      mercado: this.service.market(this.campeonato()),
    }).subscribe({
      next: ({ visao, mercado }) => {
        this.estado.set({ tipo: 'pronto', visao, mercado });
        this.title.setTitle(`Mercado · ${visao.competitionName}`);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected comprar(item: MarketItem): void {
    this.operar(
      item,
      this.service.buy(this.campeonato(), item.kind, item.id),
      'entrou no seu elenco',
      this.daEscalacao() ? (visao) => this.voltarParaEscalacao(item, visao) : undefined,
    );
  }

  protected vender(item: MarketItem): void {
    this.operar(
      item,
      this.service.sell(this.campeonato(), item.kind, item.id),
      'saiu do seu elenco',
    );
  }

  private operar(
    item: MarketItem,
    operacao: ReturnType<FantasyService['buy']>,
    resultado: string,
    concluir?: (visao: FantasyOverview) => void,
  ): void {
    this.operando.set(item.id);
    this.falhaNaOperacao.set(null);
    operacao.subscribe({
      next: (visao) => {
        if (concluir) {
          concluir(visao);
          return;
        }
        this.anuncio.set(
          `${item.name} ${resultado}. Saldo: ${credits(visao.entry?.balance ?? 0)}.`,
        );
        this.recarregarMercado(visao);
      },
      error: (falha: ApiFailure) => {
        this.operando.set(null);
        this.falhaNaOperacao.set(fantasyRefusalText(falha, this.visao()?.teamLimit));
        // Mercado fechado ou elenco alterado em outra aba: o que a tela mostra ficou velho.
        if (falha.code === FANTASY_MARKET_CLOSED_CODE || falha.code === FANTASY_CONFLICT_CODE) {
          this.carregar();
        }
      },
    });
  }

  /** A compra saiu de uma vaga do campo: volta para ele dizendo onde o ativo entrou. */
  private voltarParaEscalacao(item: MarketItem, visao: FantasyOverview): void {
    const papel = visao.entry?.slots.find(
      (slot) => slot.kind === item.kind && slot.assetId === item.id,
    )?.role;
    const onde =
      papel === 'Bench' ? 'no banco' : papel === 'Coach' ? 'como técnico' : 'como titular';
    this.notice.deixar(
      `${item.name} entrou ${onde}. Saldo: ${credits(visao.entry?.balance ?? 0)}.`,
    );
    void this.router.navigate(['/c', this.campeonato(), 'escalacao']);
  }

  /** Os bloqueios de todos os itens dependem do elenco novo, então o mercado é relido. */
  private recarregarMercado(visao: FantasyOverview): void {
    this.service.market(this.campeonato()).subscribe({
      next: (mercado) => {
        this.estado.set({ tipo: 'pronto', visao, mercado });
        this.operando.set(null);
      },
      error: (falha: ApiFailure) => {
        this.operando.set(null);
        this.estado.set({ tipo: 'erro', falha });
      },
    });
  }
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase('pt-BR');
}

function comparar(a: MarketItem, b: MarketItem, ordem: Ordem): number {
  if (ordem !== 'nome' && a.price !== b.price) {
    return ordem === 'menor' ? a.price - b.price : b.price - a.price;
  }
  return posicaoIndice(a) - posicaoIndice(b) || a.name.localeCompare(b.name, 'pt-BR');
}

/** Técnico vem depois dos atletas, que seguem a ordem do campo: do gol ao ataque. */
function posicaoIndice(item: MarketItem): number {
  return item.kind === 'Coach' || item.position === null
    ? POSITION_ORDER.length
    : POSITION_ORDER.indexOf(item.position);
}
