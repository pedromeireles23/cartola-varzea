import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { filter } from 'rxjs';

import { Alert, Badge, Button, Card, Loading } from '../../../shared/ui';
import { STATUS_LABELS } from '../competition.service';
import { CompetitionContext } from './competition-context';

/** Estado de navegação que a criação deixa para a primeira abertura do campeonato. */
export interface CompetitionCreatedState {
  readonly criado: true;
}

/**
 * Casca da área de um campeonato na organização (02 §9.1, `/organizar/c/:campeonato`).
 *
 * Tem navegação própria, separada da área de jogo, e mantém o nome do campeonato
 * sempre visível. Itens que o auxiliar não pode usar ficam ocultos aqui e bloqueados
 * na API; a casca não decide permissão, só evita oferecer o que vai ser recusado.
 */
@Component({
  selector: 'app-competition-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Alert, Badge, Button, Card, Loading, RouterLink, RouterLinkActive, RouterOutlet],
  providers: [CompetitionContext],
  template: `
    @switch (contexto.estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Abrindo o campeonato…" />
        </app-card>
      }

      @case ('erro') {
        <p class="trilha"><a routerLink="/organizar">← Minhas organizações</a></p>
        <h1>Campeonato</h1>
        <app-card>
          @if (contexto.falha()!.status === 403 || contexto.falha()!.status === 404) {
            <app-alert tone="warning">
              Este campeonato não existe ou não pertence a uma organização sua.
            </app-alert>
          } @else {
            <app-alert tone="danger">
              <p>{{ contexto.falha()!.message }}</p>
              @if (contexto.falha()!.traceId) {
                <p class="trace">Código de rastreio: {{ contexto.falha()!.traceId }}</p>
              }
            </app-alert>
            <app-button variant="secondary" (pressed)="contexto.load(campeonato())">
              Tentar de novo
            </app-button>
          }
        </app-card>
      }

      @case ('pronto') {
        <nav class="trilha" aria-label="Trilha">
          <ol>
            <li><a routerLink="/organizar">Minhas organizações</a></li>
            <li>
              <a [routerLink]="organizacaoLink()">{{ dados()!.organizationName }}</a>
            </li>
            <li aria-current="page">{{ dados()!.name }}</li>
          </ol>
        </nav>

        <div class="area">
          <header class="area__topo">
            <p class="area__nome">{{ dados()!.name }}</p>
            <p class="area__detalhe">
              <app-badge [tone]="dados()!.status === 'Draft' ? 'warning' : 'success'">
                {{ status() }}
              </app-badge>
              <span>Temporada {{ dados()!.season }}</span>
            </p>
          </header>

          <nav class="area__nav" aria-label="Navegação do campeonato">
            <a
              routerLink="."
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              [routerLinkActiveOptions]="{ exact: true }"
              class="area__link"
            >
              Resumo
            </a>
            <a
              routerLink="fases"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Fases
            </a>
            <a
              routerLink="rodadas"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Rodadas
            </a>
            <a
              routerLink="times"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Times
            </a>
            <a
              routerLink="atletas"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Atletas
            </a>
            <a
              routerLink="tecnicos"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Técnicos
            </a>
            <a
              routerLink="importacoes"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Importações
            </a>
            <a
              routerLink="publicacao"
              routerLinkActive="area__link--ativo"
              ariaCurrentWhenActive="page"
              class="area__link"
            >
              Publicação
            </a>
            @if (contexto.proprietario()) {
              <a
                routerLink="configuracao"
                routerLinkActive="area__link--ativo"
                ariaCurrentWhenActive="page"
                class="area__link"
              >
                Configuração
              </a>
            }
          </nav>

          <div class="area__conteudo">
            <router-outlet />
          </div>
        </div>
      }
    }
  `,
  styleUrl: './competition-layout.scss',
})
export class CompetitionLayout implements OnInit {
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly contexto = inject(CompetitionContext);

  /** Identificador do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly dados = this.contexto.campeonato;
  protected readonly organizacaoLink = computed(() => [
    '/organizar/o',
    this.dados()?.organizationId ?? '',
    'campeonatos',
  ]);

  protected readonly status = computed(() => {
    const atual = this.dados();
    return atual ? STATUS_LABELS[atual.status] : '';
  });

  constructor() {
    // O estado da navegação só existe enquanto ela acontece; o resumo nasce depois do
    // carregamento, então quem guarda o aviso é o contexto.
    const estado = this.router.currentNavigation()?.extras.state as
      CompetitionCreatedState | undefined;
    if (estado?.criado === true) {
      this.contexto.marcarCriado();
    }

    // O título da aba reflete o campeonato ativo (02 §8), não só o nome da tela.
    effect(() => {
      const nome = this.dados()?.name;
      if (nome) {
        this.aplicarTitulo(nome);
      }
    });

    this.router.events
      .pipe(
        filter((evento) => evento instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        // O roteador grava o título da rota logo depois deste evento.
        queueMicrotask(() => {
          const nome = this.dados()?.name;
          if (nome) {
            this.aplicarTitulo(nome);
          }
        });
      });
  }

  ngOnInit(): void {
    this.contexto.load(this.campeonato());
  }

  private aplicarTitulo(nome: string): void {
    let rota: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    let tela: string | undefined;
    while (rota) {
      tela = rota.title ?? tela;
      rota = rota.firstChild;
    }

    this.title.setTitle(tela ? `${tela} · ${nome}` : nome);
  }
}
