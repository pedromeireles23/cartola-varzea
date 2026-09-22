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

import { ApiFailure } from '../../core/api/problem-details';
import { Alert, Button, Card, Loading } from '../../shared/ui';
import { formationText } from '../organizer/competition-area/competition-format';
import { credits, points } from './fantasy-format';
import { FantasyNav } from './fantasy-nav';
import { FantasyOverview, FantasyRoundSummary, FantasyService } from './fantasy.service';
import { MarketClock } from './market-clock';

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly visao: FantasyOverview }
  | { readonly tipo: 'erro'; readonly falha: ApiFailure };

/**
 * Início do jogo no campeonato (02 §9.1, `/c/:campeonato/jogar`).
 *
 * Quem ainda não entrou vê o que recebe ao entrar e um único botão; entrar de novo é
 * inofensivo, porque o servidor devolve a participação que já existe. Quem já entrou vê
 * o estado do mercado, o saldo e o que falta para a escalação valer.
 */
@Component({
  selector: 'app-fantasy-play',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Button, Card, FantasyNav, Loading, MarketClock, RouterLink],
  template: `
    <p class="intro"><a [routerLink]="['/c', campeonato()]">← Página do campeonato</a></p>
    <app-fantasy-nav [campeonato]="campeonato()" />

    @switch (estado().tipo) {
      @case ('carregando') {
        <app-card><app-loading label="Abrindo o seu jogo…" /></app-card>
      }
      @case ('erro') {
        <h1>Jogar</h1>
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
        <h1>{{ visao()!.competitionName }}</h1>

        <app-card heading="Mercado">
          <app-market-clock [market]="visao()!.market" (closed)="carregar()" />
        </app-card>

        @if (visao()!.entry; as entrada) {
          <div #aviso tabindex="-1" class="aviso">
            @if (recemChegado()) {
              <app-alert tone="success">
                Você entrou no campeonato e recebeu {{ creditos(visao()!.profile.budget) }}.
              </app-alert>
            }
          </div>

          <app-card heading="Seu elenco">
            <dl class="dados">
              <div class="dados__item">
                <dt>Saldo</dt>
                <dd>{{ creditos(entrada.balance) }}</dd>
              </div>
              <div class="dados__item">
                <dt>Patrimônio</dt>
                <dd>{{ creditos(entrada.patrimony) }}</dd>
              </div>
              <div class="dados__item">
                <dt>Atletas</dt>
                <dd>{{ atletas() }} de {{ visao()!.profile.squadAthletes }}</dd>
              </div>
              <div class="dados__item">
                <dt>Técnico</dt>
                <dd>{{ tecnico() ?? 'Ainda não escolhido' }}</dd>
              </div>
            </dl>

            @if (entrada.issues.length === 0) {
              <app-alert tone="success">
                Escalação completa. Ela vale para a rodada quando o mercado fechar.
              </app-alert>
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
            }

            @if (rodadas().length > 0) {
              <section class="rodadas" aria-labelledby="rodadas-titulo">
                <h3 id="rodadas-titulo" class="pendencias__titulo">Rodadas apuradas</h3>
                <ul>
                  @for (rodada of rodadas(); track rodada.roundId) {
                    <li class="rodadas__item">
                      <a [routerLink]="['/c', campeonato(), 'pontuacao', rodada.roundId]">
                        {{ rodada.roundName }}
                      </a>
                      <span>
                        {{ rodada.total === null ? 'Você não jogou' : pontos(rodada.total) }}
                        @if (rodada.underCorrection) {
                          · em correção
                        } @else if (rodada.provisional) {
                          · provisório
                        }
                      </span>
                    </li>
                  }
                </ul>
              </section>
            }

            <div class="acoes-da-tela">
              <a class="acao" [routerLink]="['/c', campeonato(), 'escalacao']">Escalar meu time</a>
              <a class="acao acao--secundaria" [routerLink]="['/c', campeonato(), 'mercado']"
                >Abrir o mercado</a
              >
            </div>
          </app-card>
        } @else {
          <app-card heading="Entre no campeonato">
            <p>
              Ao entrar você recebe {{ creditos(visao()!.profile.budget) }} de orçamento para montar
              um elenco de {{ visao()!.profile.squadAthletes }} atletas e 1 técnico.
            </p>
            <dl class="dados">
              <div class="dados__item">
                <dt>Titulares</dt>
                <dd>{{ visao()!.profile.starters }}: {{ formacao() }}</dd>
              </div>
              <div class="dados__item">
                <dt>Banco</dt>
                <dd>1 reserva por posição</dd>
              </div>
              <div class="dados__item">
                <dt>Limite por time</dt>
                <dd>
                  Até {{ visao()!.teamLimit.maxAthletes }} atletas do mesmo time, sendo
                  {{ visao()!.teamLimit.maxStarters }} titulares
                </dd>
              </div>
            </dl>
            <p class="apoio">
              Os créditos são virtuais: não há pagamento, aposta nem prêmio. Entrar depois que um
              mercado fechou vale a partir da rodada seguinte.
            </p>
            @if (falhaAoEntrar(); as mensagem) {
              <app-alert tone="danger">{{ mensagem }}</app-alert>
            }
            <app-button [loading]="entrando()" (pressed)="entrar()">
              Entrar no campeonato
            </app-button>
          </app-card>
        }
      }
    }
  `,
  styleUrl: './fantasy.scss',
})
export class FantasyPlayPage implements OnInit {
  private readonly service = inject(FantasyService);
  private readonly title = inject(Title);

  /** Slug do campeonato na rota. */
  readonly campeonato = input.required<string>();

  private readonly aviso = viewChild<ElementRef<HTMLElement>>('aviso');

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly rodadas = signal<readonly FantasyRoundSummary[]>([]);
  protected readonly entrando = signal(false);
  protected readonly recemChegado = signal(false);
  protected readonly falhaAoEntrar = signal<string | null>(null);

  protected readonly visao = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.visao : null;
  });

  protected readonly formacao = computed(() => {
    const visao = this.visao();
    return visao ? formationText(visao.profile) : '';
  });

  protected readonly atletas = computed(
    () => this.visao()?.entry?.slots.filter((slot) => slot.kind === 'Athlete').length ?? 0,
  );

  protected readonly tecnico = computed(
    () => this.visao()?.entry?.slots.find((slot) => slot.kind === 'Coach')?.name ?? null,
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

  protected pontos(valor: number): string {
    return points(valor);
  }

  protected carregar(): void {
    this.service.overview(this.campeonato()).subscribe({
      next: (visao) => {
        this.mostrar(visao);
        this.carregarRodadas(visao);
      },
      error: (falha: ApiFailure) => this.estado.set({ tipo: 'erro', falha }),
    });
  }

  protected entrar(): void {
    this.entrando.set(true);
    this.falhaAoEntrar.set(null);
    this.service.join(this.campeonato()).subscribe({
      next: (visao) => {
        this.entrando.set(false);
        this.recemChegado.set(true);
        this.mostrar(visao);
        this.carregarRodadas(visao);
        // O cartão de adesão some; o foco vai para a confirmação que o substitui.
        setTimeout(() => this.aviso()?.nativeElement.focus());
      },
      error: (falha: ApiFailure) => {
        this.entrando.set(false);
        this.falhaAoEntrar.set(falha.message);
      },
    });
  }

  /**
   * Só faz sentido listar rodadas apuradas para quem está no campeonato. Quem entrou
   * agora também as vê, com o aviso de que não jogou as que já saíram.
   */
  private carregarRodadas(visao: FantasyOverview): void {
    if (!visao.entry) {
      return;
    }

    this.service.rounds(this.campeonato()).subscribe({
      next: (rodadas) => this.rodadas.set(rodadas),
      error: () => undefined,
    });
  }

  private mostrar(visao: FantasyOverview): void {
    this.estado.set({ tipo: 'pronto', visao });
    this.title.setTitle(`Jogar · ${visao.competitionName}`);
  }
}
