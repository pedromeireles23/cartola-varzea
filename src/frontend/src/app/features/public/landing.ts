import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChevronRight } from 'lucide';

import { PageMetaService } from '../../core/seo/page-meta';
import { Icon, Loading } from '../../shared/ui';
import { MODALITY_LABELS } from '../organizer/competition.service';
import { PublicCompetitionService, PublicCompetitionSummary } from './public-competition.service';

/** Quantos campeonatos cabem no destaque sem virar uma lista de busca. */
const DESTAQUES = 3;

type Estado =
  | { readonly tipo: 'carregando' }
  | { readonly tipo: 'pronto'; readonly campeonatos: readonly PublicCompetitionSummary[] }
  | { readonly tipo: 'erro' };

/**
 * Porta de entrada do produto (02 §9, `/`).
 *
 * Quem chega aqui não sabe o que é o projeto, então a página responde três coisas nesta
 * ordem: o que dá para fazer, como funciona e onde começar. A frase conceitual do 02 §1
 * — monte, dispute, acompanhe — é o próprio título, porque ela já descreve o produto
 * inteiro em três palavras.
 *
 * O destaque vem da mesma busca pública da lista: um campeonato publicado é a prova viva
 * de que a proposta existe, e sem nenhum publicado a página diz isso em vez de fingir.
 */
@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, Loading, RouterLink],
  template: `
    <section class="capa">
      <!-- Linhas de campo, geometricas e sem marca (02 §7): decoram sem competir. -->
      <svg class="capa__campo" viewBox="0 0 400 240" aria-hidden="true" focusable="false">
        <rect x="1" y="1" width="398" height="238" rx="8" />
        <circle cx="200" cy="120" r="46" />
        <path d="M200 1V239" />
        <path d="M1 60H70V180H1" />
        <path d="M399 60H330V180H399" />
      </svg>

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
        <a class="acao" routerLink="/campeonatos">Ver campeonatos</a>
        <a class="acao acao--secundaria" href="#como-funciona">Como funciona</a>
      </div>
      <p class="capa__nota">
        Créditos virtuais, sem pagamento, aposta ou prêmio. É uma demonstração de portfólio com
        dados fictícios.
      </p>
    </section>

    <section class="passos" id="como-funciona" aria-labelledby="como-funciona-titulo">
      <h2 id="como-funciona-titulo">Como funciona</h2>
      <ol class="passos__lista">
        @for (passo of passos; track passo.titulo; let i = $index) {
          <li class="passo" [style.--indice]="i">
            <span class="passo__numero" aria-hidden="true">{{ i + 1 }}</span>
            <h3 class="passo__titulo">{{ passo.titulo }}</h3>
            <p class="passo__texto">{{ passo.texto }}</p>
          </li>
        }
      </ol>
    </section>

    <section class="destaques" aria-labelledby="destaques-titulo">
      <div class="destaques__topo">
        <h2 id="destaques-titulo">Campeonatos publicados</h2>
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
          @if (campeonatos().length === 0) {
            <p class="apoio">
              Nenhum campeonato publicado ainda. Quando a primeira liga publicar o dela, ele aparece
              aqui.
            </p>
          } @else {
            <ul class="cartoes-link">
              @for (campeonato of campeonatos(); track campeonato.slug; let i = $index) {
                <li class="cartao-link" [style.--indice]="i">
                  <h3 class="cartao-link__titulo">
                    <a [routerLink]="['/c', campeonato.slug]">{{ campeonato.name }}</a>
                  </h3>
                  <p class="cartao-link__detalhe">
                    {{ rotulo(campeonato.modality) }} · Temporada {{ campeonato.season }} ·
                    {{ campeonato.organizationName }}
                  </p>
                  <svg class="cartao-link__seta" [appIcon]="seta" />
                </li>
              }
            </ul>
          }
        }
      }
    </section>
  `,
  styleUrls: ['../../shared/ui/link-card.scss', './landing.scss'],
})
export class LandingPage implements OnInit {
  protected readonly seta = ChevronRight;
  private readonly service = inject(PublicCompetitionService);
  private readonly meta = inject(PageMetaService);

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });

  protected readonly campeonatos = computed(() => {
    const atual = this.estado();
    return atual.tipo === 'pronto' ? atual.campeonatos : [];
  });

  protected readonly passos = [
    {
      titulo: 'Monte seu elenco',
      texto:
        'Escolha atletas e o técnico dentro de um orçamento em créditos virtuais. O campo é o ponto de partida: cada vaga abre o mercado já filtrado pela posição.',
    },
    {
      titulo: 'O jogo acontece',
      texto:
        'A liga lança a súmula da rodada do jeito que já faz hoje — placar, quem entrou em campo, gols, assistências, defesas e cartões.',
    },
    {
      titulo: 'Os pontos saem',
      texto:
        'Cada evento vira pontuação, e quem joga acima da média da posição valoriza. Todo total abre o detalhamento: nenhum número é caixa-preta.',
    },
  ] as const;

  ngOnInit(): void {
    this.meta.set({
      title: 'Fantasy para campeonatos de várzea',
      description:
        'Monte seu elenco com atletas do campeonato amador, acompanhe a pontuação de cada rodada e dispute o ranking com quem joga junto. Demonstração de portfólio com dados fictícios.',
    });
    this.carregar();
  }

  protected rotulo(modality: PublicCompetitionSummary['modality']): string {
    return MODALITY_LABELS[modality];
  }

  private carregar(): void {
    this.service.search('').subscribe({
      next: (campeonatos) =>
        this.estado.set({ tipo: 'pronto', campeonatos: campeonatos.slice(0, DESTAQUES) }),
      error: () => this.estado.set({ tipo: 'erro' }),
    });
  }
}
