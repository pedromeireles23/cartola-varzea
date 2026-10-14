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

import { Alert, BackLink, Badge, Card, FailureState, Loading } from '../../../shared/ui';
import { STATUS_LABELS } from '../competition.service';
import { ORGANIZER_PAGES, OrganizerArea } from '../organizer-area';
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
 *
 * No desktop, nome e navegação moram na barra lateral da casca da aplicação, que lê o
 * campeonato de `OrganizerArea`; aqui eles só aparecem abaixo de 1024 px, onde não há
 * barra lateral. As duas navegações têm o mesmo nome acessível e nunca estão visíveis
 * ao mesmo tempo.
 */
@Component({
  selector: 'app-competition-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'theme-organizer' },
  imports: [
    Alert,
    BackLink,
    Badge,
    Card,
    FailureState,
    Loading,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  providers: [CompetitionContext],
  template: `
    @switch (contexto.estado().tipo) {
      @case ('carregando') {
        <app-card>
          <app-loading label="Abrindo o campeonato…" />
        </app-card>
      }

      @case ('erro') {
        <app-back-link link="/organizar" label="Meus campeonatos" />
        <h1>Campeonato</h1>
        <app-card>
          @if (contexto.falha()!.status === 403 || contexto.falha()!.status === 404) {
            <app-alert tone="warning">
              Este campeonato não existe ou não pertence a uma organização sua.
            </app-alert>
          } @else {
            <app-failure-state
              [failure]="contexto.falha()!"
              (retry)="contexto.load(campeonato())"
            />
          }
        </app-card>
      }

      @case ('pronto') {
        <nav class="trilha" aria-label="Trilha">
          <ol>
            <li><a routerLink="/organizar">Meus campeonatos</a></li>
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
              <span>{{ dados()!.organizationName }} · temporada {{ dados()!.season }}</span>
            </p>
          </header>

          <nav class="area__nav" aria-label="Navegação do campeonato">
            @for (pagina of paginas(); track pagina.path) {
              <a
                [routerLink]="pagina.path || '.'"
                routerLinkActive="area__link--ativo"
                ariaCurrentWhenActive="page"
                [routerLinkActiveOptions]="{ exact: pagina.path === '' }"
                class="area__link"
              >
                {{ pagina.label }}
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
  private readonly area = inject(OrganizerArea);

  /** Identificador do campeonato na rota. */
  readonly campeonato = input.required<string>();

  protected readonly dados = this.contexto.campeonato;
  /** O auxiliar não vê o que é só do proprietário; a API recusa do mesmo jeito. */
  protected readonly paginas = computed(() =>
    ORGANIZER_PAGES.filter((pagina) => !pagina.ownerOnly || this.contexto.proprietario()),
  );

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

    // A barra lateral acompanha o campeonato aberto, inclusive depois de renomeá-lo.
    effect(() => {
      const dados = this.dados();
      if (dados) {
        this.area.enter({
          id: dados.id,
          name: dados.name,
          status: dados.status,
          organizationId: dados.organizationId,
          organizationName: dados.organizationName,
          owner: dados.viewerRole === 'Owner',
        });
      }
    });
    this.destroyRef.onDestroy(() => this.area.leave(this.campeonato()));

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
