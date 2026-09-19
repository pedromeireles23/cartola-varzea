import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
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
import { Observable } from 'rxjs';

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, Dialog, Loading } from '../../shared/ui';
import {
  assetRoleLabel,
  assetSlug,
  closingText,
  credits,
  fantasyRefusalText,
  positionGroupLabel,
} from './fantasy-format';
import { FantasyNav } from './fantasy-nav';
import { FantasyNotice } from './fantasy-notice';
import {
  FANTASY_CONFLICT_CODE,
  FANTASY_MARKET_CLOSED_CODE,
  FantasyOverview,
  FantasyService,
} from './fantasy.service';
import {
  Campo,
  LinhaDoCampo,
  Ocupante,
  Vaga,
  doElenco,
  doRetrato,
  formacaoCurta,
  montarCampo,
  reservaDaPosicao,
  titularesDaPosicao,
} from './lineup-model';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/** Abreviação visível na vaga; o nome completo vai junto para o leitor de tela. */
const SIGLAS = {
  Goalkeeper: 'GOL',
  Defender: 'DEF',
  Midfielder: 'MEI',
  Forward: 'ATA',
} as const;

/**
 * Escalação no campo (02 §8 "Lineup pitch" e §9.1, `/c/:campeonato/escalacao`).
 *
 * O campo é o ponto de partida da montagem: cada vaga vazia abre o mercado filtrado
 * pela posição, e a compra volta para cá. Nada depende de arrastar — cada atleta abre
 * um diálogo com as ações que as regras permitem: capitão, troca com o banco e venda.
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
    Card,
    Dialog,
    FantasyNav,
    Loading,
    MarketClock,
    NgTemplateOutlet,
    RouterLink,
  ],
  template: `
    <app-fantasy-nav [campeonato]="campeonato()" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <h1>Escalação</h1>
        <app-card><app-loading label="Abrindo a escalação…" /></app-card>
      }
      @case ('erro') {
        <h1>Escalação</h1>
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
        <h1>Escalação</h1>
        <p class="intro">{{ visao()!.competitionName }}</p>

        <app-card>
          <app-market-clock [market]="visao()!.market" (closed)="carregar()" />
        </app-card>

        @let campoAtual = campo()!;
        @if (visao()!.entry; as entrada) {
          <div #caixaDeAviso tabindex="-1" class="aviso">
            @if (aviso(); as texto) {
              <app-alert tone="success">{{ texto }}</app-alert>
            }
          </div>
          @if (falhaGeral(); as mensagem) {
            <app-alert tone="danger">{{ mensagem }}</app-alert>
          }

          @if (editavel()) {
            <section class="resumo" aria-label="Seu elenco">
              <p class="resumo__item">
                <span class="resumo__rotulo">Saldo</span>
                <strong>{{ creditos(entrada.balance) }}</strong>
              </p>
              <p class="resumo__item">
                <span class="resumo__rotulo">Atletas</span>
                <strong>{{ atletas() }} de {{ visao()!.profile.squadAthletes }}</strong>
              </p>
              <p class="resumo__item">
                <span class="resumo__rotulo">Técnico</span>
                <strong>{{ campoAtual.tecnico.ocupante ? '1 de 1' : '0 de 1' }}</strong>
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
          } @else if (fechada(); as rodada) {
            @switch (rodada.status) {
              @case ('Frozen') {
                <app-alert tone="success">
                  Escalação congelada para a {{ rodada.roundName }} desde
                  {{ horario(rodada.marketClosedAtLocal) }}. É ela que vale na apuração.
                </app-alert>
              }
              @case ('Incomplete') {
                <app-alert tone="warning">
                  Sua escalação não estava completa quando o mercado da
                  {{ rodada.roundName }} fechou, {{ horario(rodada.marketClosedAtLocal) }}. Você
                  fica fora dessa rodada e volta a escalar quando o mercado da próxima abrir.
                </app-alert>
              }
              @case ('JoinedAfterClose') {
                <app-alert tone="info">
                  Você entrou depois do fechamento da {{ rodada.roundName }}. Seu jogo começa na
                  próxima rodada com o mercado aberto.
                </app-alert>
              }
            }
          }

          <section class="campo" aria-labelledby="titulares-titulo">
            <h2 id="titulares-titulo" class="campo__titulo">
              Titulares <span class="campo__formacao">{{ formacao() }}</span>
            </h2>
            @for (linha of campoAtual.linhas; track linha.posicao) {
              <ul class="campo__linha" [attr.aria-label]="rotuloDaLinha(linha)">
                @for (vaga of linha.vagas; track vaga.chave) {
                  <li class="campo__vaga">
                    <ng-container *ngTemplateOutlet="vagaTpl; context: { $implicit: vaga }" />
                  </li>
                }
              </ul>
            }
          </section>

          <section class="banco" aria-labelledby="banco-titulo">
            <h2 id="banco-titulo" class="banco__titulo">Banco</h2>
            <p class="apoio">O reserva só entra no lugar de alguém da mesma posição.</p>
            <ul class="banco__vagas">
              @for (vaga of campoAtual.banco; track vaga.chave) {
                <li class="campo__vaga">
                  <ng-container *ngTemplateOutlet="vagaTpl; context: { $implicit: vaga }" />
                </li>
              }
            </ul>
          </section>

          <section class="banco" aria-labelledby="tecnico-titulo">
            <h2 id="tecnico-titulo" class="banco__titulo">Técnico</h2>
            <p class="apoio">Faz a média dos atletas do time dele que jogaram a rodada.</p>
            <div class="banco__tecnico">
              <ng-container
                *ngTemplateOutlet="vagaTpl; context: { $implicit: campoAtual.tecnico }"
              />
            </div>
          </section>

          @if (editavel()) {
            <app-card heading="Resumo da escalação">
              @if (entrada.issues.length === 0) {
                <app-alert tone="success">
                  Escalação completa. Ela vale para a {{ visao()!.market.roundName }} e congela
                  {{ horario(visao()!.market.closesAtLocal) }}.
                </app-alert>
                <dl class="dados">
                  <div class="dados__item">
                    <dt>Capitão</dt>
                    <dd>{{ capitao() }}</dd>
                  </div>
                  <div class="dados__item">
                    <dt>Técnico</dt>
                    <dd>{{ campoAtual.tecnico.ocupante?.name }}</dd>
                  </div>
                  <div class="dados__item">
                    <dt>Valor do elenco</dt>
                    <dd>{{ creditos(entrada.patrimony - entrada.balance) }}</dd>
                  </div>
                  <div class="dados__item">
                    <dt>Saldo</dt>
                    <dd>{{ creditos(entrada.balance) }}</dd>
                  </div>
                </dl>
              } @else {
                <section class="pendencias" aria-labelledby="pendencias-titulo">
                  <h3 id="pendencias-titulo" class="pendencias__titulo">
                    O que falta para a escalação valer
                  </h3>
                  <ul>
                    @for (pendencia of entrada.issues; track pendencia.message) {
                      <li>{{ pendencia.message }}</li>
                    }
                  </ul>
                </section>
                <p class="apoio">
                  Se o mercado fechar com algo faltando, você fica fora da
                  {{ visao()!.market.roundName }}.
                </p>
              }
              <p class="apoio">
                Cada mudança já fica salva. Até o fechamento, dá para trocar à vontade.
              </p>
            </app-card>
          }
        } @else {
          <app-card heading="Entre no campeonato">
            <p>Para escalar, entre no campeonato primeiro: é lá que você recebe o orçamento.</p>
            <a class="acao" [routerLink]="['/c', campeonato(), 'jogar']">Entrar no campeonato</a>
          </app-card>
        }

        <app-dialog
          [open]="selecionada() !== null"
          [heading]="selecionada()?.ocupante?.name ?? ''"
          (dismissed)="fecharAcoes()"
        >
          @if (selecionada(); as vaga) {
            <p class="acoes__descricao">
              {{ descricao(vaga) }} · {{ vaga.ocupante!.realTeamName }} ·
              {{ creditos(vaga.ocupante!.price) }}
            </p>
            @if (vaga.ocupante!.isCaptain) {
              <p class="acoes__descricao">É o capitão: dobra os pontos na rodada.</p>
            }
            @if (falhaNaAcao(); as mensagem) {
              <app-alert tone="danger">{{ mensagem }}</app-alert>
            }
            <div class="acoes">
              @if (vaga.papel === 'Starter' && !vaga.ocupante!.isCaptain) {
                <app-button
                  [fullWidth]="true"
                  [disabled]="operando()"
                  (pressed)="tornarCapitao(vaga)"
                >
                  Tornar capitão
                </app-button>
              }
              @if (vaga.papel === 'Starter') {
                @if (reservaDe(vaga); as reserva) {
                  <app-button
                    variant="secondary"
                    [fullWidth]="true"
                    [disabled]="operando()"
                    (pressed)="trocar(vaga, reserva)"
                  >
                    Trocar com {{ reserva.ocupante!.name }}, do banco
                  </app-button>
                }
              }
              @if (vaga.papel === 'Bench') {
                @for (titular of titularesDe(vaga); track titular.chave) {
                  <app-button
                    variant="secondary"
                    [fullWidth]="true"
                    [disabled]="operando()"
                    (pressed)="trocar(titular, vaga)"
                  >
                    Entrar no lugar de {{ titular.ocupante!.name }}
                  </app-button>
                }
              }
              <app-button
                variant="danger"
                [fullWidth]="true"
                [disabled]="operando()"
                (pressed)="vender(vaga)"
              >
                Vender por {{ creditos(vaga.ocupante!.price) }}
              </app-button>
            </div>
          }
          <div dialogActions>
            <app-button variant="ghost" (pressed)="fecharAcoes()">Fechar</app-button>
          </div>
        </app-dialog>
      }
    }

    <ng-template #vagaTpl let-vaga>
      @if (vaga.ocupante; as ocupante) {
        @if (editavel()) {
          <button
            type="button"
            class="vaga"
            [class.vaga--capitao]="ocupante.isCaptain"
            [class.vaga--indisponivel]="!ocupante.isAvailable"
            (click)="abrirAcoes(vaga)"
          >
            <ng-container *ngTemplateOutlet="conteudoTpl; context: { $implicit: vaga }" />
          </button>
        } @else {
          <div
            class="vaga vaga--leitura"
            [class.vaga--capitao]="ocupante.isCaptain"
            [class.vaga--indisponivel]="!ocupante.isAvailable"
          >
            <ng-container *ngTemplateOutlet="conteudoTpl; context: { $implicit: vaga }" />
          </div>
        }
      } @else if (editavel()) {
        <a
          class="vaga vaga--vazia"
          [routerLink]="['/c', campeonato(), 'mercado']"
          [queryParams]="{ posicao: slugDaVaga(vaga), origem: 'escalacao' }"
        >
          <span class="sr-only">Escolher {{ minusculas(descricao(vaga)) }}</span>
          <span class="vaga__sigla" aria-hidden="true">{{ sigla(vaga) }}</span>
          <span class="vaga__nome" aria-hidden="true">Escolher</span>
        </a>
      } @else {
        <div class="vaga vaga--vazia vaga--leitura">
          <span class="sr-only">Vaga vazia de {{ minusculas(descricao(vaga)) }}</span>
          <span class="vaga__sigla" aria-hidden="true">{{ sigla(vaga) }}</span>
          <span class="vaga__nome" aria-hidden="true">Vaga vazia</span>
        </div>
      }
    </ng-template>

    <!--
      O leitor de tela lê uma frase só, com vírgulas; as partes visuais ficam escondidas
      dele para que nome, time e preço não saiam colados.
    -->
    <ng-template #conteudoTpl let-vaga>
      <span class="sr-only">{{ rotuloAcessivel(vaga) }}</span>
      <span class="vaga__sigla" aria-hidden="true">{{ sigla(vaga) }}</span>
      <span class="vaga__nome" aria-hidden="true">{{ vaga.ocupante.name }}</span>
      <span class="vaga__detalhe" aria-hidden="true">{{ vaga.ocupante.realTeamName }}</span>
      <span class="vaga__detalhe" aria-hidden="true">{{ creditos(vaga.ocupante.price) }}</span>
      @if (vaga.ocupante.isCaptain) {
        <span class="vaga__capitao" aria-hidden="true">C</span>
      }
      @if (!vaga.ocupante.isAvailable) {
        <span class="vaga__alerta" aria-hidden="true">Indisponível</span>
      }
    </ng-template>
  `,
  styleUrls: ['./fantasy.scss', './fantasy-lineup.scss'],
})
export class FantasyLineupPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly notice = inject(FantasyNotice);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  private readonly avisoRef = viewChild<ElementRef<HTMLElement>>('caixaDeAviso');

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly selecionada = signal<Vaga | null>(null);
  protected readonly operando = signal(false);
  protected readonly aviso = signal<string | null>(null);
  protected readonly falhaNaAcao = signal<string | null>(null);
  protected readonly falhaGeral = signal<string | null>(null);

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

  protected readonly atletas = computed(
    () => this.visao()?.entry?.slots.filter((slot) => slot.kind === 'Athlete').length ?? 0,
  );

  protected readonly capitao = computed(
    () => this.visao()?.entry?.slots.find((slot) => slot.isCaptain)?.name ?? 'Ainda não escolhido',
  );

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

  protected rotuloDaLinha(linha: LinhaDoCampo): string {
    return positionGroupLabel(linha.posicao, linha.vagas.length);
  }

  protected sigla(vaga: Vaga): string {
    return vaga.posicao === null ? 'TEC' : SIGLAS[vaga.posicao];
  }

  /** "Goleiro titular", "Reserva de defensor" ou "Técnico": o papel da vaga por extenso. */
  protected descricao(vaga: Vaga): string {
    if (vaga.papel === 'Coach' || vaga.posicao === null) {
      return 'Técnico';
    }
    const posicao = assetRoleLabel('Athlete', vaga.posicao);
    return vaga.papel === 'Bench'
      ? `Reserva de ${posicao.toLocaleLowerCase('pt-BR')}`
      : `${posicao} titular`;
  }

  /** "Atacante titular: Bia, União da Vila, C$ 8,00, capitão". */
  protected rotuloAcessivel(vaga: Vaga): string {
    const ocupante = vaga.ocupante!;
    return [
      `${this.descricao(vaga)}: ${ocupante.name}`,
      ocupante.realTeamName,
      credits(ocupante.price),
      ...(ocupante.isCaptain ? ['capitão'] : []),
      ...(ocupante.isAvailable ? [] : ['indisponível']),
    ].join(', ');
  }

  protected minusculas(texto: string): string {
    return texto.toLocaleLowerCase('pt-BR');
  }

  protected slugDaVaga(vaga: Vaga): string {
    return assetSlug(vaga.papel === 'Coach' ? 'Coach' : 'Athlete', vaga.posicao);
  }

  protected reservaDe(vaga: Vaga): Vaga | null {
    return reservaDaPosicao(this.campo()!, vaga.posicao);
  }

  protected titularesDe(vaga: Vaga): Vaga[] {
    return titularesDaPosicao(this.campo()!, vaga.posicao);
  }

  protected abrirAcoes(vaga: Vaga): void {
    this.falhaNaAcao.set(null);
    this.selecionada.set(vaga);
  }

  protected fecharAcoes(): void {
    this.selecionada.set(null);
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

  protected carregar(): void {
    this.service.overview(this.campeonato()).subscribe({
      next: (visao) => {
        this.mostrar(visao);
        // A compra feita a partir de uma vaga volta para cá com o recado do mercado.
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
        this.selecionada.set(null);
        this.mostrar(visao);
        this.anunciar(mensagem(visao));
      },
      error: (falha: ApiFailure) => {
        this.operando.set(false);
        // Mercado fechado ou elenco alterado em outra aba: o campo ficou velho.
        if (falha.code === FANTASY_MARKET_CLOSED_CODE || falha.code === FANTASY_CONFLICT_CODE) {
          this.selecionada.set(null);
          this.falhaGeral.set(falha.message);
          this.carregar();
          return;
        }
        // Recusa de regra (limite do time, por exemplo): fica no diálogo, junto da ação.
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
    this.title.setTitle(`Escalação · ${visao.competitionName}`);
  }
}
