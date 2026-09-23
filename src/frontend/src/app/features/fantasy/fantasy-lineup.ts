import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { CircleAlert, CircleCheck, LayoutGrid, List, X } from 'lucide';
import { Observable } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Icon, Loading, PageHeader } from '../../shared/ui';
import { closingText, credits, fantasyRefusalText } from './fantasy-format';
import { FantasyNotice } from './fantasy-notice';
import {
  FANTASY_CONFLICT_CODE,
  FANTASY_MARKET_CLOSED_CODE,
  FantasyMarket,
  FantasyOverview,
  FantasyService,
  MarketItem,
} from './fantasy.service';
import { LineupBoard, LineupView, descricaoDaVaga } from './lineup-board';
import {
  Campo,
  Ocupante,
  Vaga,
  doElenco,
  doRetrato,
  formacaoCurta,
  montarCampo,
  reservaDaPosicao,
  titularesDaPosicao,
} from './lineup-model';
import { LineupPicker } from './lineup-picker';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** A partir daqui o painel fica ao lado do campo; abaixo, sobe como folha (06 §4.3). */
const DESKTOP = '(min-width: 1024px)';

/** Onde fica a preferência de ver em campo ou em lista; só conveniência deste navegador. */
const CHAVE_DO_MODO = 'cv.escalacao-modo';

function lerModo(): LineupView {
  try {
    return globalThis.localStorage?.getItem(CHAVE_DO_MODO) === 'lista' ? 'lista' : 'campo';
  } catch {
    return 'campo';
  }
}

/**
 * Meu time (02 §8 "Lineup pitch", 06 §4.2, `/c/:campeonato/escalacao`).
 *
 * O campo é o lugar de montar o time: a vaga vazia abre a escolha já filtrada pela
 * posição e a vaga ocupada abre as ações do atleta — capitão, troca com o banco, venda —
 * no mesmo painel. No desktop ele fica ao lado do campo, sem esconder o time; no celular
 * sobe de baixo, com botão Fechar à vista. Nada depende de arrastar.
 *
 * O elenco é a própria escalação (decisão de 2026-09-18): cada ação já fica gravada no
 * servidor, por isso não existe botão de salvar. Quando o mercado fecha, o servidor
 * congela o que estava completo; a tela passa a mostrar esse retrato, com os nomes do
 * fechamento, ou explica por que a conta ficou fora da rodada.
 */
@Component({
  selector: 'app-fantasy-lineup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Alert,
    Button,
    Icon,
    LineupBoard,
    LineupPicker,
    Loading,
    MarketClock,
    PageHeader,
    RouterLink,
  ],
  templateUrl: './fantasy-lineup.html',
  styleUrls: ['./fantasy.scss', './fantasy-lineup.scss'],
})
export class FantasyLineupPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly notice = inject(FantasyNotice);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  private readonly avisoRef = viewChild<ElementRef<HTMLElement>>('caixaDeAviso');
  private readonly painelRef = viewChild<ElementRef<HTMLDialogElement>>('painel');

  protected readonly icons = {
    campo: LayoutGrid,
    lista: List,
    fechar: X,
    pendente: CircleAlert,
    feito: CircleCheck,
  } as const;

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly modo = signal<LineupView>(lerModo());
  protected readonly selecionada = signal<Vaga | null>(null);
  protected readonly operando = signal(false);
  /** Id do item sendo comprado no seletor, para o botão dele mostrar o carregamento. */
  protected readonly comprando = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);
  protected readonly falhaNaAcao = signal<string | null>(null);
  protected readonly falhaGeral = signal<string | null>(null);
  protected readonly mercado = signal<FantasyMarket | null>(null);
  protected readonly mercadoFalhou = signal(false);

  private readonly desktop = signal(false);
  /** Quem abriu o painel, para o foco voltar a ele quando o painel fechar. */
  private gatilho: HTMLElement | null = null;

  protected readonly visao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.visao : null;
  });

  /** Com o mercado aberto e a conta dentro, o campo é o elenco corrente e aceita ações. */
  protected readonly editavel = computed(
    () => this.visao()?.market.isOpen === true && this.visao()?.entry != null,
  );

  /** A rodada que fechou por último, mostrada enquanto o mercado está fechado. */
  protected readonly fechada = computed(() =>
    this.editavel() ? null : (this.visao()?.entry?.lastClosedRound ?? null),
  );

  /**
   * Mercado fechado com a escalação congelada: o campo mostra o retrato, que guarda os
   * nomes do fechamento. Nos outros casos, o elenco corrente.
   */
  protected readonly campo = computed<Campo | null>(() => {
    const visao = this.visao();
    if (!visao) {
      return null;
    }
    const retrato = this.fechada();
    const ocupantes: Ocupante[] =
      retrato?.status === 'Frozen'
        ? retrato.slots.map(doRetrato)
        : (visao.entry?.slots ?? []).map(doElenco);
    return montarCampo(visao.profile, ocupantes);
  });

  protected readonly formacao = computed(() => {
    const visao = this.visao();
    return visao ? formacaoCurta(visao.profile) : '';
  });

  protected readonly subtitulo = computed(() => {
    const visao = this.visao();
    if (!visao) {
      return null;
    }
    return visao.market.roundName
      ? `${visao.competitionName} · ${visao.market.roundName}`
      : visao.competitionName;
  });

  protected readonly escolhidos = computed(() => this.visao()?.entry?.slots.length ?? 0);

  protected readonly vagas = computed(() => {
    const perfil = this.visao()?.profile;
    return perfil ? perfil.squadAthletes + 1 : 0;
  });

  protected readonly progresso = computed(() =>
    this.vagas() === 0 ? 0 : Math.round((this.escolhidos() / this.vagas()) * 100),
  );

  protected readonly capitao = computed(
    () => this.visao()?.entry?.slots.find((slot) => slot.isCaptain)?.name ?? null,
  );

  protected readonly tecnico = computed(
    () => this.visao()?.entry?.slots.find((slot) => slot.kind === 'Coach')?.name ?? null,
  );

  protected readonly tituloDoPainel = computed(() => {
    const vaga = this.selecionada();
    if (!vaga) {
      return '';
    }
    return vaga.ocupante ? vaga.ocupante.name : `Escolher ${this.minusculas(this.descricao(vaga))}`;
  });

  constructor() {
    const consulta = globalThis.matchMedia?.(DESKTOP);
    if (consulta) {
      this.desktop.set(consulta.matches);
      const mudou = (evento: MediaQueryListEvent) => this.desktop.set(evento.matches);
      consulta.addEventListener('change', mudou);
      inject(DestroyRef).onDestroy(() => consulta.removeEventListener('change', mudou));
    }
  }

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

  protected horario(local: string | null): string {
    const visao = this.visao();
    return local && visao ? closingText(local, visao.market.timeZoneId) : '';
  }

  protected descricao(vaga: Vaga): string {
    return descricaoDaVaga(vaga);
  }

  protected minusculas(texto: string): string {
    return texto.toLocaleLowerCase('pt-BR');
  }

  protected mudarModo(modo: LineupView): void {
    this.modo.set(modo);
    try {
      globalThis.localStorage?.setItem(CHAVE_DO_MODO, modo);
    } catch {
      // Sem armazenamento, a escolha vale até sair da página.
    }
  }

  protected reservaDe(vaga: Vaga): Vaga | null {
    return reservaDaPosicao(this.campo()!, vaga.posicao);
  }

  protected titularesDe(vaga: Vaga): Vaga[] {
    return titularesDaPosicao(this.campo()!, vaga.posicao);
  }

  /** A vaga tocada no campo abre o painel: escolha, se vazia; ações, se ocupada. */
  protected abrir(vaga: Vaga): void {
    this.falhaNaAcao.set(null);
    const painel = this.painelRef()?.nativeElement;
    if (!painel?.open) {
      this.gatilho = document.activeElement as HTMLElement | null;
    }
    this.selecionada.set(vaga);
    if (!vaga.ocupante && this.mercado() === null) {
      this.carregarMercado();
    }
    if (painel && !painel.open) {
      // No desktop o painel não bloqueia o campo: dá para tocar outra vaga com ele aberto.
      if (this.desktop()) {
        painel.show();
        setTimeout(() => painel.querySelector<HTMLElement>('input, button')?.focus());
      } else {
        painel.showModal();
      }
    }
  }

  protected fecharPainel(): void {
    this.painelRef()?.nativeElement.close();
  }

  /** Fechou por Esc, pelo botão ou pelo fundo: o foco volta para a vaga que abriu. */
  protected aoFecharPainel(): void {
    this.selecionada.set(null);
    const gatilho = this.gatilho;
    this.gatilho = null;
    if (gatilho?.isConnected) {
      gatilho.focus();
    }
  }

  protected fecharPeloFundo(evento: MouseEvent): void {
    if (evento.target === this.painelRef()?.nativeElement) {
      this.fecharPainel();
    }
  }

  protected tornarCapitao(vaga: Vaga): void {
    const ocupante = vaga.ocupante!;
    this.executar(
      this.service.captain(this.campeonato(), ocupante.assetId),
      () => `${ocupante.name} é o capitão.`,
    );
  }

  protected trocar(titular: Vaga, reserva: Vaga): void {
    const sai = titular.ocupante!;
    const entra = reserva.ocupante!;
    this.executar(
      this.service.swap(this.campeonato(), sai.assetId, entra.assetId),
      () =>
        `${entra.name} entrou no lugar de ${sai.name}, que foi para o banco.` +
        (sai.isCaptain ? ' Escolha outro capitão.' : ''),
    );
  }

  protected vender(vaga: Vaga): void {
    const ocupante = vaga.ocupante!;
    this.executar(
      this.service.sell(this.campeonato(), ocupante.kind, ocupante.assetId),
      (visao) =>
        `${ocupante.name} saiu do seu elenco. Saldo: ${credits(visao.entry?.balance ?? 0)}.`,
    );
  }

  /** O servidor decide o papel de quem entra; o recado diz onde ele ficou. */
  protected comprar(item: MarketItem): void {
    this.comprando.set(item.id);
    this.executar(this.service.buy(this.campeonato(), item.kind, item.id), (visao) => {
      const papel = visao.entry?.slots.find(
        (slot) => slot.kind === item.kind && slot.assetId === item.id,
      )?.role;
      const onde =
        papel === 'Bench' ? 'no banco' : papel === 'Coach' ? 'como técnico' : 'como titular';
      return `${item.name} entrou ${onde}. Saldo: ${credits(visao.entry?.balance ?? 0)}.`;
    });
  }

  protected carregarMercado(): void {
    this.mercadoFalhou.set(false);
    this.service.market(this.campeonato()).subscribe({
      next: (mercado) => this.mercado.set(mercado),
      error: () => this.mercadoFalhou.set(true),
    });
  }

  protected carregar(): void {
    this.service.overview(this.campeonato()).subscribe({
      next: (visao) => {
        this.mostrar(visao);
        // A compra feita pelo mercado completo volta para cá com o recado dele.
        const recado = this.notice.retirar();
        if (recado) {
          this.anunciar(recado);
        }
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  private executar(
    operacao: Observable<FantasyOverview>,
    mensagem: (visao: FantasyOverview) => string,
  ): void {
    this.operando.set(true);
    this.falhaNaAcao.set(null);
    this.falhaGeral.set(null);
    operacao.subscribe({
      next: (visao) => {
        this.operando.set(false);
        this.comprando.set(null);
        // Saldo e limites mudaram: o que o seletor sabia do mercado ficou velho.
        this.mercado.set(null);
        this.mostrar(visao);
        this.gatilho = null;
        this.fecharPainel();
        this.anunciar(mensagem(visao));
      },
      error: (falha: ApiFailure) => {
        this.operando.set(false);
        this.comprando.set(null);
        // Mercado fechado ou elenco alterado em outra aba: o campo ficou velho.
        if (falha.code === FANTASY_MARKET_CLOSED_CODE || falha.code === FANTASY_CONFLICT_CODE) {
          this.gatilho = null;
          this.fecharPainel();
          this.falhaGeral.set(falha.message);
          this.carregar();
          return;
        }
        // Recusa de regra (limite do time, por exemplo): fica no painel, junto da ação.
        this.falhaNaAcao.set(fantasyRefusalText(falha, this.visao()?.teamLimit));
      },
    });
  }

  /** O foco vai para o aviso: a vaga que tinha o foco pode ter deixado de existir. */
  private anunciar(texto: string): void {
    this.aviso.set(texto);
    setTimeout(() => this.avisoRef()?.nativeElement.focus());
  }

  private mostrar(visao: FantasyOverview): void {
    this.estado.set({ tipo: 'pronto', visao });
    this.title.setTitle(`Meu time · ${visao.competitionName}`);
  }
}
