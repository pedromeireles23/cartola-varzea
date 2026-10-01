import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ChevronRight, ClipboardList, Shirt, Trophy } from 'lucide';

import { AuthService } from '../../core/auth/auth.service';
import { PageMetaService } from '../../core/seo/page-meta';
import { Alert, Button, Icon, Loading } from '../../shared/ui';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { LandingFeatured } from './landing-featured';
import { PublicCompetitionService, PublicCompetitionSummary } from './public-competition.service';

/** Além do destaque, quantos campeonatos cabem na vitrine sem virar a lista de busca. */
const OUTROS = 3;

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonatos: readonly PublicCompetitionSummary[] }
  | { readonly tipo: 'erro' };

interface Ficha {
  readonly sigla: string;
  /** Posição no gramado, em porcentagem: `x` da esquerda, `y` do fundo do campo. */
  readonly x: number;
  readonly y: number;
  readonly capitao?: boolean;
}

/**
 * Porta de entrada do produto (02 §9, `/`), na direção Noite de jogo (V5).
 *
 * Quem chega aqui não sabe o que é o projeto, então a página responde, nesta ordem: o
 * que dá para fazer, onde isso já está acontecendo e como funciona. A frase conceitual
 * do 02 §1 — monte, dispute, acompanhe — continua sendo o título, e o aviso de que não
 * há aposta em dinheiro vem antes dele, para ninguém confundir o placar com uma casa de
 * apostas.
 *
 * O campeonato em destaque é o mais recente publicado, com placar e ranking de verdade:
 * a prova viva de que a proposta existe. Sem nenhum publicado, a página diz isso em vez
 * de fingir. Na demonstração, a entrada de visitante fica aqui também, a um clique.
 */
@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'theme-player' },
  imports: [Alert, Button, Icon, LandingFeatured, Loading, RouterLink],
  template: `
    <section class="capa">
      <div class="capa__copy">
        <p class="capa__selo">
          <span class="capa__selo-marca">Fantasy de futebol amador</span>
          <span class="capa__selo-aviso">Sem apostas em dinheiro</span>
        </p>

        <!--
          O espaco vai dentro de cada span: entre tags, o Angular remove o no de texto em
          branco, e o titulo chegaria ao leitor de tela como "Monte,dispute,acompanhe.".
          Visualmente ele some, porque cada palavra e um item da grade.
        -->
        <h1 class="capa__titulo">
          <span>{{ 'Monte, ' }}</span>
          <span>{{ 'dispute, ' }}</span>
          <span>acompanhe.</span>
        </h1>
        <p class="capa__texto">
          O fantasy do campeonato que você já joga ou assiste. Escale atletas de verdade da várzea,
          some os pontos da súmula da rodada e dispute com quem está no grupo.
        </p>

        <div class="capa__acoes">
          @if (logado()) {
            <a class="acao" routerLink="/inicio">Ir para o início</a>
            <a class="acao acao--secundaria" routerLink="/campeonatos">Ver campeonatos</a>
          } @else if (demo()) {
            <app-button [loading]="entrando()" (pressed)="entrarComoVisitante()">
              Entrar como visitante
            </app-button>
            <a class="acao acao--secundaria" routerLink="/campeonatos">Ver campeonatos</a>
          } @else {
            <a class="acao" routerLink="/campeonatos">Ver campeonatos</a>
          }
          <a class="capa__link" href="#como-funciona">Como funciona</a>
        </div>
        @if (erroVisitante()) {
          <app-alert tone="danger">{{ erroVisitante() }}</app-alert>
        }

        <p class="capa__nota">
          Créditos virtuais, sem pagamento, aposta ou prêmio. É uma demonstração de portfólio com
          dados fictícios.
        </p>
      </div>

      <!--
        O estádio é ilustração: um gramado noturno em perspectiva, com uma escalação de
        Fut7 e a placa de LED do fundo. Nada nele é dado, então ele fica fora da árvore
        de acessibilidade; o aviso da placa já está, em texto, no selo acima do título.
      -->
      <div class="estadio" aria-hidden="true">
        <p class="estadio__placa">
          <span>Fantasy esportivo</span>
          <span class="estadio__placa-destaque">Sem apostas em dinheiro</span>
          <span>Créditos virtuais</span>
        </p>
        <div class="estadio__gramado">
          <svg class="estadio__linhas" viewBox="0 0 300 400" focusable="false">
            <rect x="6" y="6" width="288" height="388" />
            <path d="M6 200H294" />
            <circle cx="150" cy="200" r="38" />
            <path d="M82 6V62H218V6" />
            <path d="M118 6V26H182V6" />
            <path d="M82 394V338H218V394" />
            <path d="M118 394V374H182V394" />
          </svg>
          @for (ficha of fichas; track $index) {
            <span
              class="estadio__ficha"
              [class.estadio__ficha--capitao]="ficha.capitao"
              [style.left.%]="ficha.x"
              [style.top.%]="ficha.y"
              [style.--ordem]="$index"
            >
              {{ ficha.sigla }}
            </span>
          }
        </div>
      </div>
    </section>

    <section class="vitrine" aria-labelledby="vitrine-titulo">
      <div class="secao__topo">
        <h2 id="vitrine-titulo" class="secao__titulo">Campeonatos publicados</h2>
        <a routerLink="/campeonatos">Ver todos</a>
      </div>

      @switch (estado().tipo) {
        @case ('carregando') {
          <app-loading label="Buscando campeonatos…" />
        }
        @case ('erro') {
          <p class="apoio">
            Não deu para carregar os campeonatos agora.
            <a routerLink="/campeonatos">Tente pela lista completa</a>.
          </p>
        }
        @case ('pronto') {
          @if (destaque(); as campeonato) {
            <app-landing-featured [campeonato]="campeonato" />
            @if (outros().length > 0) {
              <ul class="cartoes-link">
                @for (outro of outros(); track outro.slug; let i = $index) {
                  <li class="cartao-link" [style.--indice]="i">
                    <h3 class="cartao-link__titulo">
                      <a [routerLink]="['/c', outro.slug]">{{ outro.name }}</a>
                    </h3>
                    <p class="cartao-link__detalhe">
                      {{ rotulo(outro.modality) }} · Temporada {{ outro.season }} ·
                      {{ outro.organizationName }}
                    </p>
                    <svg class="cartao-link__seta" [appIcon]="seta" />
                  </li>
                }
              </ul>
            }
          } @else {
            <p class="apoio">
              Nenhum campeonato publicado ainda. Quando a primeira liga publicar o dela, ele aparece
              aqui.
            </p>
          }
        }
      }
    </section>

    <section class="passos" id="como-funciona" aria-labelledby="como-funciona-titulo">
      <h2 id="como-funciona-titulo" class="secao__titulo">Como funciona</h2>
      <ol class="passos__lista">
        @for (passo of passos; track passo.titulo; let i = $index) {
          <li class="passo" [style.--indice]="i">
            <span class="passo__topo" aria-hidden="true">
              <span class="passo__numero">{{ i + 1 }}</span>
              <svg class="passo__icone" [appIcon]="passo.icone" [size]="28" />
            </span>
            <h3 class="passo__titulo">{{ passo.titulo }}</h3>
            <p class="passo__texto">{{ passo.texto }}</p>
          </li>
        }
      </ol>
    </section>
  `,
  styleUrls: ['../../shared/ui/link-card.scss', './landing-hero.scss', './landing.scss'],
})
export class LandingPage implements OnInit {
  protected readonly seta = ChevronRight;
  private readonly service = inject(PublicCompetitionService);
  private readonly meta = inject(PageMetaService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  /** Quem já tem sessão vai para o próprio início, não para a entrada de visitante. */
  protected readonly logado = this.auth.isAuthenticated;

  /** Só a demonstração pública oferece a entrada de visitante. */
  protected readonly demo = signal(false);
  protected readonly entrando = signal(false);
  protected readonly erroVisitante = signal<string | null>(null);

  protected readonly campeonatos = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonatos : [];
  });

  protected readonly destaque = computed(() => this.campeonatos()[0] ?? null);
  protected readonly outros = computed(() => this.campeonatos().slice(1, 1 + OUTROS));

  /** Um 1-2-2-2 de Fut7, com o capitão no ataque: só ilustração. */
  protected readonly fichas: readonly Ficha[] = [
    { sigla: 'GOL', x: 50, y: 88 },
    { sigla: 'DEF', x: 30, y: 70 },
    { sigla: 'DEF', x: 70, y: 70 },
    { sigla: 'MEI', x: 24, y: 47 },
    { sigla: 'MEI', x: 76, y: 47 },
    { sigla: 'ATA', x: 38, y: 24 },
    { sigla: 'ATA', x: 62, y: 24, capitao: true },
  ];

  protected readonly passos = [
    {
      titulo: 'Monte seu elenco',
      icone: Shirt,
      texto:
        'Escolha atletas e o técnico dentro de um orçamento em créditos virtuais. O campo é o ponto de partida: cada vaga abre o mercado já filtrado pela posição.',
    },
    {
      titulo: 'O jogo acontece',
      icone: ClipboardList,
      texto:
        'A liga lança a súmula da rodada do jeito que já faz hoje — placar, quem entrou em campo, gols, assistências, defesas e cartões.',
    },
    {
      titulo: 'Os pontos saem',
      icone: Trophy,
      texto:
        'Cada evento vira pontuação, e quem joga acima da média da posição valoriza. Todo total abre o detalhamento: nenhum número é caixa-preta.',
    },
  ] as const;

  ngOnInit(): void {
    this.meta.set({
      title: 'Fantasy para campeonatos de várzea',
      description:
        'Monte seu elenco com atletas do campeonato amador, acompanhe a pontuação de cada rodada e dispute o ranking com quem joga junto. Créditos virtuais, sem apostas em dinheiro. Demonstração de portfólio com dados fictícios.',
    });
    this.carregar();

    if (!this.logado()) {
      void this.auth.demoAvailable().then((disponivel) => this.demo.set(disponivel));
    }
  }

  protected rotulo(modality: PublicCompetitionSummary['modality']): string {
    return MODALITY_LABELS[modality];
  }

  protected async entrarComoVisitante(): Promise<void> {
    this.entrando.set(true);
    this.erroVisitante.set(null);

    try {
      await this.auth.enterAsVisitor();
      await this.router.navigateByUrl('/inicio');
    } catch {
      this.erroVisitante.set('A entrada de visitante não está disponível agora.');
    } finally {
      this.entrando.set(false);
    }
  }

  private carregar(): void {
    this.service.search('').subscribe({
      next: (campeonatos) => this.estado.set({ tipo: 'pronto', campeonatos }),
      error: () => this.estado.set({ tipo: 'erro' }),
    });
  }
}
