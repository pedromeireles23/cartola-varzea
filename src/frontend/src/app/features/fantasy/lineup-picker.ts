import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Search } from 'lucide';

import { Alert, Button, Icon, Loading } from '../../shared/ui';
import { assetSlug, credits, teamInitials } from './fantasy-format';
import { MarketItem } from './fantasy.service';
import { Vaga } from './lineup-model';

type Ordem = 'menor' | 'maior' | 'nome';

/** Bloqueios que valem para o mercado inteiro: com a tela editável, nunca aparecem aqui. */
const BLOQUEIOS_GERAIS = new Set(['not_joined', 'market_closed']);

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase('pt-BR');
}

/**
 * Escolha de quem entra numa vaga, sem sair do campo (06 §3.1).
 *
 * Antes, cada vaga vazia abria o mercado em outra página e a compra voltava para cá —
 * doze idas e voltas para montar um time. Aqui a lista já vem filtrada pela posição da
 * vaga, com busca por nome ou time e ordem por preço; quem não cabe no saldo ou passa do
 * limite do time continua visível, com o motivo, para a pessoa entender a regra em vez de
 * achar que o atleta sumiu. O servidor decide de novo na compra.
 */
@Component({
  selector: 'app-lineup-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Icon, Loading, RouterLink],
  template: `
    <div class="ferramentas">
      <label class="busca">
        <span class="sr-only">Buscar por nome ou time</span>
        <svg class="busca__icone" [appIcon]="icons.search" />
        <input
          class="busca__campo"
          type="search"
          placeholder="Nome ou time"
          [value]="busca()"
          (input)="busca.set(valor($event))"
        />
      </label>
      <label class="ordem">
        <span class="sr-only">Ordenar</span>
        <select class="ordem__campo" (change)="ordem.set($any(valor($event)))">
          <option value="menor" [selected]="ordem() === 'menor'">Menor preço</option>
          <option value="maior" [selected]="ordem() === 'maior'">Maior preço</option>
          <option value="nome" [selected]="ordem() === 'nome'">Nome</option>
        </select>
      </label>
    </div>

    <p class="contagem">
      <span role="status">{{ contagem() }}</span> · saldo
      <strong class="num">{{ creditos(saldo()) }}</strong>
    </p>

    @if (falha(); as mensagem) {
      <app-alert tone="danger">{{ mensagem }}</app-alert>
    }

    @if (falhouAoCarregar()) {
      <app-alert tone="danger">Não deu para abrir o mercado agora.</app-alert>
      <app-button variant="secondary" (pressed)="recarregar.emit()">Tentar de novo</app-button>
    } @else if (itens() === null) {
      <app-loading label="Buscando atletas…" />
    } @else if (opcoes().length === 0) {
      <p class="vazio">
        {{
          busca() ? 'Ninguém com esse nome nesta posição.' : 'Ninguém disponível para esta vaga.'
        }}
      </p>
    } @else {
      <ul class="opcoes">
        @for (item of opcoes(); track item.id) {
          <li class="opcao" [class.opcao--bloqueada]="item.blockCode !== null">
            <span class="opcao__escudo" aria-hidden="true">{{ iniciais(item.realTeamName) }}</span>
            <span class="opcao__texto">
              <span class="opcao__nome">{{ item.name }}</span>
              <span class="opcao__time">
                {{ item.realTeamName }}{{ item.isAvailable ? '' : ' · indisponível' }}
              </span>
              @if (motivo(item); as texto) {
                <span class="opcao__motivo">{{ texto }}</span>
              }
            </span>
            <span class="opcao__preco num">{{ creditos(item.price) }}</span>
            <app-button
              variant="secondary"
              [loading]="operando() === item.id"
              [disabled]="item.blockCode !== null || operando() !== null"
              (pressed)="comprar.emit(item)"
            >
              Comprar<span class="sr-only"> {{ item.name }}</span>
            </app-button>
          </li>
        }
      </ul>
    }

    <a
      class="mercado"
      [routerLink]="['/c', campeonato(), 'mercado']"
      [queryParams]="{ posicao: posicaoNaUrl() }"
    >
      Ver no mercado completo
    </a>
  `,
  styleUrl: './lineup-picker.scss',
})
export class LineupPicker {
  readonly vaga = input.required<Vaga>();
  readonly campeonato = input.required<string>();
  /** Nulo enquanto o mercado carrega. */
  readonly itens = input<readonly MarketItem[] | null>(null);
  readonly saldo = input(0);
  readonly operando = input<string | null>(null);
  readonly falha = input<string | null>(null);
  readonly falhouAoCarregar = input(false);

  readonly comprar = output<MarketItem>();
  readonly recarregar = output<void>();

  protected readonly icons = { search: Search } as const;
  protected readonly busca = signal('');
  protected readonly ordem = signal<Ordem>('menor');

  protected readonly posicaoNaUrl = computed(() => {
    const vaga = this.vaga();
    return assetSlug(vaga.papel === 'Coach' ? 'Coach' : 'Athlete', vaga.posicao);
  });

  /** Da posição da vaga, fora do elenco; quem pode ser comprado vem antes. */
  protected readonly opcoes = computed(() => {
    const vaga = this.vaga();
    const termo = normalizar(this.busca().trim());
    const ordem = this.ordem();
    return (this.itens() ?? [])
      .filter(
        (item) =>
          !item.isOwned &&
          (vaga.papel === 'Coach'
            ? item.kind === 'Coach'
            : item.kind === 'Athlete' && item.position === vaga.posicao) &&
          (termo === '' ||
            normalizar(item.name).includes(termo) ||
            normalizar(item.realTeamName).includes(termo)),
      )
      .sort(
        (a, b) =>
          Number(a.blockCode !== null) - Number(b.blockCode !== null) ||
          (ordem === 'nome'
            ? a.name.localeCompare(b.name, 'pt-BR')
            : ordem === 'menor'
              ? a.price - b.price
              : b.price - a.price) ||
          a.name.localeCompare(b.name, 'pt-BR'),
      );
  });

  protected readonly contagem = computed(() => {
    if (this.itens() === null) {
      return 'Carregando';
    }
    const total = this.opcoes().length;
    return `${total} ${total === 1 ? 'opção' : 'opções'}`;
  });

  protected valor(evento: Event): string {
    return (evento.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected creditos(valor: number): string {
    return credits(valor);
  }

  protected iniciais(time: string): string {
    return teamInitials(time);
  }

  protected motivo(item: MarketItem): string | null {
    return item.blockCode === null || BLOQUEIOS_GERAIS.has(item.blockCode)
      ? null
      : item.blockReason;
  }
}
