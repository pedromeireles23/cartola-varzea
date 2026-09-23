import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { FantasyService, MyFantasyCompetition } from '../fantasy/fantasy.service';

/** Área do produto em que a pessoa está, para marcar a navegação (06 §4). */
export type Section =
  | 'home'
  | 'competitions'
  | 'team'
  | 'market'
  | 'standings'
  | 'rounds'
  | 'rules'
  | 'account'
  | 'organize';

/** Onde a escolha sobrevive a um recarregamento; é conveniência, nunca fonte de verdade. */
const STORAGE_KEY = 'cv.campeonato-atual';

/** Rotas do jogo dentro de `/c/:campeonato/...` e a seção a que cada uma pertence. */
const GAME_SECTIONS: Readonly<Record<string, Section>> = {
  escalacao: 'team',
  mercado: 'market',
  ranking: 'standings',
  ligas: 'standings',
  rodadas: 'rounds',
  pontuacao: 'rounds',
  partidas: 'rounds',
};

/** Seção de cada rota do jogo, para trocar de campeonato sem trocar de tela. */
export const SECTION_PATHS: Readonly<Partial<Record<Section, string>>> = {
  team: 'escalacao',
  market: 'mercado',
  standings: 'ranking',
  rounds: 'rodadas',
};

const TOP_SECTIONS: Readonly<Record<string, Section>> = {
  inicio: 'home',
  campeonatos: 'competitions',
  regras: 'rules',
  perfil: 'account',
  organizar: 'organize',
  admin: 'organize',
};

function segmentsOf(url: string): string[] {
  const path = url.split(/[?#]/)[0] ?? '';
  return path.split('/').filter(Boolean).map(decodeURIComponent);
}

function readChoice(): string | null {
  try {
    return globalThis.localStorage?.getItem(STORAGE_KEY) ?? null;
  } catch {
    return null;
  }
}

function saveChoice(slug: string): void {
  try {
    globalThis.localStorage?.setItem(STORAGE_KEY, slug);
  } catch {
    // Sem armazenamento (aba privada, bloqueio): a escolha vale só nesta visita.
  }
}

/**
 * O campeonato em que a pessoa está jogando agora (06 §4.2).
 *
 * A navegação do campeonato só existe depois de entrar em um: a lista vem de
 * `GET /api/v1/fantasy`. O atual é o da rota, quando a pessoa participa dele; senão, o
 * último escolhido; senão, o primeiro da lista, que o servidor ordena pelo mais mexido.
 */
@Injectable({ providedIn: 'root' })
export class CurrentCompetition {
  private readonly fantasy = inject(FantasyService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly list = signal<readonly MyFantasyCompetition[] | null>(null);
  private readonly failure = signal(false);
  private readonly chosen = signal<string | null>(readChoice());
  private readonly url = signal(this.router.url);

  /** Campeonatos da conta; vazio enquanto carrega ou sem sessão. */
  readonly mine = computed(() => this.list() ?? []);

  /** Verdadeiro depois da primeira resposta, para distinguir "carregando" de "nenhum". */
  readonly loaded = computed(() => this.list() !== null);

  /** A última leitura falhou: "nenhum campeonato" seria mentira, e o início diz isso. */
  readonly failed = this.failure.asReadonly();

  /** Slug de `/c/:campeonato/...`, participe a conta dele ou não. */
  readonly routeSlug = computed(() => {
    const [first, slug] = segmentsOf(this.url());
    return first === 'c' && slug ? slug : null;
  });

  readonly current = computed<MyFantasyCompetition | null>(() => {
    const mine = this.mine();
    const find = (slug: string | null) => mine.find((item) => item.slug === slug) ?? null;
    return find(this.routeSlug()) ?? find(this.chosen()) ?? mine[0] ?? null;
  });

  readonly section = computed<Section | null>(() => {
    const [first, slug, page] = segmentsOf(this.url());
    if (first === 'c' && slug) {
      return page ? (GAME_SECTIONS[page] ?? null) : null;
    }
    return first ? (TOP_SECTIONS[first] ?? null) : null;
  });

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => this.url.set(event.urlAfterRedirects));

    // A lista acompanha a sessão: entrar carrega, sair esvazia.
    effect(() => {
      if (this.auth.current()) {
        this.reload();
      } else {
        this.list.set(null);
      }
    });

    // Abrir um campeonato em que a conta joga faz dele o atual das próximas visitas.
    effect(() => {
      const slug = this.routeSlug();
      if (slug && slug !== this.chosen() && this.mine().some((item) => item.slug === slug)) {
        this.chosen.set(slug);
        saveChoice(slug);
      }
    });
  }

  /** Relê a lista; chamado depois de entrar num campeonato. */
  reload(): void {
    this.fantasy.myCompetitions().subscribe({
      next: (items) => {
        this.failure.set(false);
        this.list.set(items);
      },
      // O início explica a falha; a navegação só não mostra o campeonato.
      error: () => {
        this.failure.set(true);
        this.list.set([]);
      },
    });
  }

  /** Troca o campeonato mantendo a seção: quem estava no mercado continua no mercado. */
  async switchTo(slug: string): Promise<void> {
    this.chosen.set(slug);
    saveChoice(slug);
    const section = this.section();
    const path = section ? SECTION_PATHS[section] : undefined;
    if (path) {
      await this.router.navigate(['/c', slug, path]);
    }
  }
}
