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
  ArrowLeft,
  BookOpen,
  Building2,
  CalendarDays,
  CircleUserRound,
  ClipboardList,
  FileUp,
  Globe,
  House,
  Inbox,
  Layers,
  LayoutDashboard,
  ListOrdered,
  Menu,
  Settings,
  Shield,
  Shirt,
  ShoppingBag,
  Trophy,
  UserRound,
  Users,
  X,
  type IconNode,
} from 'lucide';
import { filter } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { CompetitionStatus } from '../../features/organizer/competition.service';
import { OrganizerArea } from '../../features/organizer/organizer-area';
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

/** Tela da área de um campeonato na organização, depois de `/organizar/c/:campeonato/`. */
interface OrganizerItem {
  readonly path: string;
  readonly label: string;
  readonly icon: IconNode;
  /** Outras rotas que pertencem à mesma tela, como a súmula dentro de Rodadas. */
  readonly also?: readonly string[];
  readonly ownerOnly?: boolean;
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

/** A mesma ordem da casca do campeonato, que é a ordem do trabalho de quem organiza. */
const ORGANIZER_ITEMS: readonly OrganizerItem[] = [
  { path: '', label: 'Resumo', icon: LayoutDashboard },
  { path: 'fases', label: 'Fases', icon: Layers },
  { path: 'rodadas', label: 'Rodadas', icon: CalendarDays, also: ['partidas'] },
  { path: 'times', label: 'Times', icon: Shield },
  { path: 'atletas', label: 'Atletas', icon: Users },
  { path: 'tecnicos', label: 'Técnicos', icon: UserRound },
  { path: 'importacoes', label: 'Importações', icon: FileUp },
  { path: 'publicacao', label: 'Publicação', icon: Globe },
  { path: 'configuracao', label: 'Configuração', icon: Settings, ownerOnly: true },
];

/** Espelha `STATUS_LABELS` sem trazer o serviço de campeonatos para o pacote inicial. */
const COMPETITION_STATUS: Readonly<Record<CompetitionStatus, string>> = {
  Draft: 'Rascunho',
  Published: 'Publicado',
};

const GLOBAL_ROUTES: Readonly<Partial<Record<Section, string>>> = {
  home: '/inicio',
  competitions: '/campeonatos',
  rules: '/regras',
  account: '/perfil',
  organize: '/organizar',
};

function segmentsOf(url: string): string[] {
  const path = url.split(/[?#]/)[0] ?? '';
  return path.split('/').filter(Boolean).map(decodeURIComponent);
}

/**
 * Casca única da aplicação (06 §4).
 *
 * Sem sessão, um cabeçalho enxuto para descobrir campeonatos. Com sessão, a navegação
 * separa o global (onde estou no produto) do contextual (o que posso fazer neste
 * campeonato): barra lateral persistente no desktop; no celular, cabeçalho compacto e
 * barra inferior com quatro destinos, em que "Mais" abre o restante numa folha.
 *
 * A organização é uma área à parte (06 §3.2 e §4.1): em `/organizar` e `/admin`, para
 * quem organiza, a barra lateral troca a navegação do jogo pela da organização e pela
 * do campeonato aberto, com a volta ao jogo à vista. Quem ainda não organiza — pedindo
 * acesso ou aceitando um convite — continua na casca do jogo.
 *
 * A navegação da organização vai no pacote inicial de todo mundo, embora só quem
 * organiza a use: medido em 2026-09-24, custa cerca de 11 kB crus, e carregá-la por
 * `@defer` trazia 6 kB do próprio mecanismo de adiamento do Angular — economia pequena
 * demais para uma barra lateral que piscaria ao chegar.
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
  private readonly organizer = inject(OrganizerArea);

  private readonly moreSheet = viewChild<ElementRef<HTMLDialogElement>>('mais');

  protected readonly account = this.auth.current;
  protected readonly globalItems = GLOBAL_ITEMS;
  protected readonly gameItems = GAME_ITEMS;
  protected readonly icons = {
    team: Shirt,
    rules: BookOpen,
    organize: ClipboardList,
    organizations: Building2,
    applications: Inbox,
    back: ArrowLeft,
    home: House,
    competitions: Trophy,
    account: CircleUserRound,
    menu: Menu,
    close: X,
  } as const;
  protected readonly section = this.competitions.section;
  protected readonly mine = this.competitions.mine;
  protected readonly current = this.competitions.current;
  protected readonly moreOpen = signal(false);
  protected readonly canOrganize = this.organizer.canOrganize;
  protected readonly isAdmin = this.auth.isPlatformAdmin;
  protected readonly organizerCompetition = this.organizer.competition;

  /** Título estático da rota mais funda, para as telas fora das seções conhecidas. */
  private readonly routeTitle = signal<string | null>(null);
  private readonly segments = signal(segmentsOf(this.router.url));

  /** A área de organização, com casca própria; só para quem organiza. */
  protected readonly organizing = computed(
    () => this.section() === 'organize' && this.canOrganize(),
  );

  protected readonly organizerItems = computed(() =>
    ORGANIZER_ITEMS.filter((item) => !item.ownerOnly || this.organizerCompetition()?.owner),
  );

  protected readonly organizerStatus = computed(() => {
    const status = this.organizerCompetition()?.status;
    return status ? COMPETITION_STATUS[status] : '';
  });

  /** Minhas organizações e as páginas de uma organização, fora de um campeonato. */
  protected readonly inOrganizations = computed(() => {
    const [first, second] = this.segments();
    return first === 'organizar' && second !== 'c';
  });

  protected readonly inApplications = computed(() => this.segments()[0] === 'admin');

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
    // Na organização, o título é o da tela: "Rodadas", "Minhas organizações".
    if (this.organizing()) {
      return this.routeTitle() ?? 'Organização';
    }
    const section = this.section();
    return section ? SECTION_LABELS[section] : (this.routeTitle() ?? 'Cartola Várzea');
  });

  /**
   * O campeonato ao lado do título. Numa rota de campeonato, só quando a conta joga nele:
   * a página pública de outro campeonato não pode parecer parte do jogo atual.
   */
  protected readonly contextName = computed(() => {
    if (this.organizing()) {
      return this.organizerCompetition()?.name ?? null;
    }
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
      .subscribe((event) => {
        this.segments.set(segmentsOf(event.urlAfterRedirects));
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

  /** A tela do campeonato aberta agora: `rodadas` também acende na súmula de uma partida. */
  protected isCurrentOrganizer(item: OrganizerItem): boolean {
    const [first, second, , page = ''] = this.segments();
    if (first !== 'organizar' || second !== 'c') return false;
    return page === item.path || (item.also?.includes(page) ?? false);
  }

  protected organizerLink(item: OrganizerItem, id: string): string[] {
    return item.path ? ['/organizar/c', id, item.path] : ['/organizar/c', id];
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
