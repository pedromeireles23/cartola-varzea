import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import {
  BookOpen,
  CalendarDays,
  CircleUserRound,
  ClipboardList,
  House,
  ListOrdered,
  Menu,
  Shirt,
  ShoppingBag,
  Trophy,
  X,
  type IconNode,
} from 'lucide';
import { filter } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { CurrentCompetition, Section } from '../../features/participant/current-competition';
import { DemoBanner } from '../demo-banner/demo-banner';
import { NotificationBell } from '../notification-bell/notification-bell';
import { Icon } from '../../shared/ui/icon';

interface NavItem {
  readonly section: Section;
  readonly label: string;
  readonly icon: IconNode;
}

interface GameItem extends NavItem {
  /** Trecho depois de `/c/:campeonato/`. */
  readonly path: string;
}

const SECTION_LABELS: Readonly<Record<Section, string>> = {
  home: 'Início',
  competitions: 'Campeonatos',
  team: 'Meu time',
  market: 'Mercado',
  standings: 'Classificação',
  rounds: 'Rodadas',
  rules: 'Regras',
  account: 'Conta',
  organize: 'Organizar',
};

const GLOBAL_ITEMS: readonly NavItem[] = [
  { section: 'home', label: 'Início', icon: House },
  { section: 'competitions', label: 'Campeonatos', icon: Trophy },
];

const GAME_ITEMS: readonly GameItem[] = [
  { section: 'team', label: 'Meu time', icon: Shirt, path: 'escalacao' },
  { section: 'market', label: 'Mercado', icon: ShoppingBag, path: 'mercado' },
  { section: 'standings', label: 'Classificação', icon: ListOrdered, path: 'ranking' },
  { section: 'rounds', label: 'Rodadas', icon: CalendarDays, path: 'rodadas' },
];

const GLOBAL_ROUTES: Readonly<Partial<Record<Section, string>>> = {
  home: '/inicio',
  competitions: '/campeonatos',
  rules: '/regras',
  account: '/perfil',
  organize: '/organizar',
};

/**
 * Casca única da aplicação (06 §4).
 *
 * Sem sessão, um cabeçalho enxuto para descobrir campeonatos. Com sessão, a navegação
 * separa o global (onde estou no produto) do contextual (o que posso fazer neste
 * campeonato): barra lateral persistente no desktop; no celular, cabeçalho compacto e
 * barra inferior com quatro destinos, em que "Mais" abre o restante numa folha.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DemoBanner, Icon, NotificationBell, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './shell.html',
  styleUrls: ['./shell.scss', './shell-mobile.scss'],
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly competitions = inject(CurrentCompetition);

  private readonly moreSheet = viewChild<ElementRef<HTMLDialogElement>>('mais');

  protected readonly account = this.auth.current;
  protected readonly globalItems = GLOBAL_ITEMS;
  protected readonly gameItems = GAME_ITEMS;
  protected readonly icons = {
    team: Shirt,
    rules: BookOpen,
    organize: ClipboardList,
    account: CircleUserRound,
    menu: Menu,
    close: X,
  } as const;
  protected readonly section = this.competitions.section;
  protected readonly mine = this.competitions.mine;
  protected readonly current = this.competitions.current;
  protected readonly moreOpen = signal(false);

  /** Título estático da rota mais funda, para as telas fora das seções conhecidas. */
  private readonly routeTitle = signal<string | null>(null);

  protected readonly canOrganize = computed(() => {
    const roles = this.account()?.roles ?? [];
    return roles.includes('Organizer') || roles.includes('PlatformAdmin');
  });

  protected readonly initials = computed(() =>
    (this.account()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0])
      .join('')
      .toLocaleUpperCase('pt-BR'),
  );

  protected readonly sectionTitle = computed(() => {
    const section = this.section();
    return section ? SECTION_LABELS[section] : (this.routeTitle() ?? 'Cartola Várzea');
  });

  /**
   * O campeonato ao lado do título. Numa rota de campeonato, só quando a conta joga nele:
   * a página pública de outro campeonato não pode parecer parte do jogo atual.
   */
  protected readonly contextName = computed(() => {
    const routeSlug = this.competitions.routeSlug();
    if (routeSlug) {
      return this.mine().find((item) => item.slug === routeSlug)?.competitionName ?? null;
    }
    return this.section() === 'home' ? (this.current()?.competitionName ?? null) : null;
  });

  /** A seção do jogo só acende quando a tela é do campeonato atual. */
  protected readonly inCurrentCompetition = computed(
    () => this.competitions.routeSlug() === this.current()?.slug,
  );

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        this.routeTitle.set(deepestTitle(this.router.routerState.snapshot.root));
        this.closeMore();
      });
  }

  protected globalRoute(section: Section): string {
    return GLOBAL_ROUTES[section] ?? '/inicio';
  }

  protected isCurrentGlobal(section: Section): boolean {
    return this.section() === section;
  }

  protected isCurrentGame(section: Section): boolean {
    return this.section() === section && this.inCurrentCompetition();
  }

  protected async switchCompetition(event: Event): Promise<void> {
    const slug = (event.target as HTMLSelectElement).value;
    await this.competitions.switchTo(slug);
  }

  protected openMore(): void {
    const sheet = this.moreSheet()?.nativeElement;
    if (sheet && !sheet.open) {
      sheet.showModal();
      this.moreOpen.set(true);
    }
  }

  protected closeMore(): void {
    const sheet = this.moreSheet()?.nativeElement;
    if (sheet?.open) {
      sheet.close();
    }
  }

  /** Toque no fundo escurecido fecha a folha, como o Esc e o botão Fechar. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.moreSheet()?.nativeElement) {
      this.closeMore();
    }
  }
}

function deepestTitle(root: ActivatedRouteSnapshot): string | null {
  let node: ActivatedRouteSnapshot | null = root;
  let title: string | null = null;
  while (node) {
    title = node.title ?? title;
    node = node.firstChild;
  }
  return title;
}
