import { NgTemplateOutlet } from '@angular/common';
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
import { Search, Shirt } from 'lucide';
import { forkJoin } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Badge, Button, Icon, Loading, PageHeader } from '../../shared/ui';
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
import { siglaDaVaga } from './lineup-board';
import { MarketClock } from './market-clock';
import { SquadMetrics } from './squad-metrics';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview; readonly mercado: FantasyMarket }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

type Posicao = AthletePosition | 'Coach' | '';
type Situacao = 'todos' | 'compraveis' | 'elenco';
/** `time` agrupa por time real; as outras ordens mostram uma lista só. */
type Ordem = 'time' | 'menor' | 'maior' | 'nome';

interface Grupo {
  readonly timeId: string;
  readonly time: string;
  readonly itens: readonly MarketItem[];
}

interface Opcao<T extends string> {
  readonly value: T;
  readonly label: string;
}

/** Times por página: o mercado de várzea costuma ter de 6 a 16 times. */
const TIMES_POR_PAGINA = 6;

/** Na lista sem agrupamento, o mesmo tanto de linhas que cabe em seis times pequenos. */
const ITENS_POR_PAGINA = 30;

/** `?posicao=` em português, como as rotas; é o que o seletor do campo manda. */
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
 * Por padrão agrupa por time real, que é como se procura atleta na várzea: primeiro o
 * time, depois a pessoa (02 §8). Ordenar por preço ou por nome desfaz o agrupamento,
 * porque aí a pergunta é outra — quem cabe no saldo — e ela se responde numa lista só.
 * A posição é um toque, não um menu, porque é o filtro que mais se troca. O motivo de um
 * item não poder ser comprado vem do servidor e aparece no próprio item; o servidor
 * continua recusando a compra mesmo se a tela for ignorada.
 */
@Component({
  selector: 'app-fantasy-market',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Alert,
    Badge,
    Button,
    Icon,
    Loading,
    MarketClock,
    NgTemplateOutlet,
    PageHeader,
    RouterLink,
    SquadMetrics,
  ],
  templateUrl: './fantasy-market.html',
  styleUrls: ['./fantasy.scss', './fantasy-market.scss'],
})
export class FantasyMarketPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly title = inject(Title);
  private readonly router = inject(Router);
  private readonly notice = inject(FantasyNotice);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  /** Filtro inicial vindo do campo de escalação: `?posicao=goleiro`. */
  readonly posicao = input<string>();

  /** `?origem=escalacao`: a vaga do campo abriu o mercado, e a compra volta para lá. */
  readonly origem = input<string>();

  protected readonly icons = { busca: Search, time: Shirt } as const;

  protected readonly opcoesPosicao: readonly Opcao<Posicao>[] = [
    { value: '', label: 'Todas' },
    ...POSITION_ORDER.map((position) => ({ value: position, label: POSITION_LABELS[position] })),
    { value: 'Coach', label: 'Técnico' },
  ];

  protected readonly opcoesSituacao: readonly Opcao<Situacao>[] = [
    { value: 'todos', label: 'Todos' },
    { value: 'compraveis', label: 'Posso comprar' },
    { value: 'elenco', label: 'No meu elenco' },
  ];

  protected readonly opcoesOrdem: readonly Opcao<Ordem>[] = [
    { value: 'time', label: 'Por time' },
    { value: 'menor', label: 'Menor preço' },
    { value: 'maior', label: 'Maior preço' },
    { value: 'nome', label: 'Nome' },
  ];

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly busca = signal('');
  protected readonly filtroPosicao = linkedSignal<Posicao>(
    () => POSICAO_NA_URL[this.posicao() ?? ''] ?? '',
  );
  protected readonly situacao = signal<Situacao>('todos');
  protected readonly ordem = signal<Ordem>('time');
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

  protected readonly subtitulo = computed(() => {
    const visao = this.visao();
    const rodada = this.mercado()?.market.roundName;
    if (!visao) {
      return null;
    }
    return rodada ? `${visao.competitionName} · ${rodada}` : visao.competitionName;
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

  protected readonly agrupado = computed(() => this.ordem() === 'time');

  private readonly filtrados = computed(() => {
    const termo = normalizar(this.busca().trim());
    const posicao = this.filtroPosicao();
    const situacao = this.situacao();
    return (this.mercado()?.items ?? []).filter(
      (item) =>
        (termo === '' ||
          normalizar(item.name).includes(termo) ||
          normalizar(item.realTeamName).includes(termo)) &&
        (posicao === '' ||
          (posicao === 'Coach' ? item.kind === 'Coach' : item.position === posicao)) &&
        (situacao === 'todos' ||
          (situacao === 'elenco' ? item.isOwned : !item.isOwned && item.blockCode === null)),
    );
  });

  protected readonly grupos = computed<readonly Grupo[]>(() => {
    const porTime = new Map<string, MarketItem[]>();
    for (const item of this.filtrados()) {
      porTime.set(item.realTeamId, [...(porTime.get(item.realTeamId) ?? []), item]);
    }
    return [...porTime.entries()]
      .map(([timeId, itens]) => ({
        timeId,
        time: itens[0]!.realTeamName,
        itens: [...itens].sort(porPosicaoENome),
      }))
      .sort((a, b) => a.time.localeCompare(b.time, 'pt-BR'));
  });

  /** A lista só, para as ordens por preço e por nome. */
  private readonly ordenados = computed(() => {
    const ordem = this.ordem();
    return [...this.filtrados()].sort((a, b) =>
      ordem === 'nome'
        ? a.name.localeCompare(b.name, 'pt-BR')
        : (ordem === 'menor' ? a.price - b.price : b.price - a.price) ||
          a.name.localeCompare(b.name, 'pt-BR'),
    );
  });

  private readonly filtro = computed(() =>
    [this.busca(), this.filtroPosicao(), this.situacao(), this.ordem()].join('|'),
  );

  /**
   * Volta à primeira página quando o filtro muda. Reler o mercado depois de uma compra
   * não muda o filtro, então a pessoa continua onde estava.
   */
  private readonly limite = linkedSignal({ source: this.filtro, computation: () => 1 });

  protected readonly gruposVisiveis = computed(() =>
    this.grupos().slice(0, this.limite() * TIMES_POR_PAGINA),
  );

  protected readonly itensVisiveis = computed(() =>
    this.ordenados().slice(0, this.limite() * ITENS_POR_PAGINA),
  );

  protected readonly restantes = computed(() =>
    this.agrupado()
      ? Math.max(0, this.grupos().length - this.gruposVisiveis().length)
      : Math.max(0, this.ordenados().length - this.itensVisiveis().length),
  );

  protected readonly contagem = computed(() => {
    const itens = this.filtrados().length;
    const opcoes = `${itens} ${itens === 1 ? 'opção' : 'opções'}`;
    if (!this.agrupado()) {
      return opcoes;
    }
    const times = this.grupos().length;
    return `${opcoes} em ${times} ${times === 1 ? 'time' : 'times'}`;
  });

  ngOnInit(): void {
    this.carregar();
  }

  protected falha(): ApiFailure | null {
    const atual = this.estado();
    return atual.tipo === 'erro' ? atual.falha : null;
  }

  protected valor(evento: Event): string {
    return (evento.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected mudarSituacao(evento: Event): void {
    this.situacao.set(this.valor(evento) as Situacao);
  }

  protected mudarOrdem(evento: Event): void {
    this.ordem.set(this.valor(evento) as Ordem);
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  protected papel(item: MarketItem): string {
    return assetRoleLabel(item.kind, item.position);
  }

  protected sigla(item: MarketItem): string {
    return siglaDaVaga({
      chave: item.id,
      papel: item.kind === 'Coach' ? 'Coach' : 'Starter',
      posicao: item.kind === 'Coach' ? null : item.position,
      ocupante: null,
    });
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
    this.limite.update((atual) => atual + 1);
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

/** Dentro do time, a ordem do campo — do gol ao ataque, técnico no fim — e o nome. */
function porPosicaoENome(a: MarketItem, b: MarketItem): number {
  return posicaoIndice(a) - posicaoIndice(b) || a.name.localeCompare(b.name, 'pt-BR');
}

function posicaoIndice(item: MarketItem): number {
  return item.kind === 'Coach' || item.position === null
    ? POSITION_ORDER.length
    : POSITION_ORDER.indexOf(item.position);
}
