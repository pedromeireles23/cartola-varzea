import { Component, computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { Account, AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { MyFantasyCompetition } from '../../features/fantasy/fantasy.service';
import { OrganizerArea, OrganizerCompetition } from '../../features/organizer/organizer-area';
import { CurrentCompetition, Section } from '../../features/participant/current-competition';
import { Shell } from './shell';

@Component({ template: '' })
class Tela {}

const CAMPEONATO: OrganizerCompetition = {
  id: 'c1',
  name: 'Copa da Vila',
  status: 'Published',
  organizationId: 'o1',
  organizationName: 'Liga da Vila',
  owner: false,
};

type FantasyCompetitionPatch = Partial<Omit<MyFantasyCompetition, 'market'>> & {
  market?: Partial<MyFantasyCompetition['market']>;
};

function fantasyCompetition(patch: FantasyCompetitionPatch = {}): MyFantasyCompetition {
  const base: MyFantasyCompetition = {
    competitionName: 'Copa da Vila',
    slug: 'copa-da-vila',
    season: '2026',
    modality: 'Fut7',
    balance: 100,
    squadSize: 5,
    squadSizeTarget: 8,
    hasCaptain: true,
    joinedAt: '2026-09-01T12:00:00Z',
    market: {
      isOpen: true,
      roundName: 'Rodada 3',
      closesAt: '2026-09-30T22:00:00Z',
      closesAtLocal: '2026-09-30T19:00:00',
      timeZoneId: 'America/Sao_Paulo',
    },
  };
  const { market, ...competition } = patch;

  return {
    ...base,
    ...competition,
    market: { ...base.market, ...(market ?? {}) },
  };
}

describe('Shell', () => {
  let conta: ReturnType<typeof signal<Account | null>>;
  let secao: ReturnType<typeof signal<Section | null>>;
  let minhas: ReturnType<typeof signal<readonly MyFantasyCompetition[]>>;
  let atual: ReturnType<typeof signal<MyFantasyCompetition | null>>;
  let slugDaRota: ReturnType<typeof signal<string | null>>;
  let trocarCampeonato: ReturnType<typeof vi.fn>;
  let organiza: ReturnType<typeof signal<boolean>>;
  let aberto: ReturnType<typeof signal<OrganizerCompetition | null>>;

  beforeEach(() => {
    conta = signal<Account | null>({
      id: 'u1',
      email: 'pessoa@exemplo.local',
      displayName: 'Pessoa Organizadora',
      emailConfirmed: true,
      roles: ['Organizer'],
    });
    secao = signal<Section | null>('organize');
    minhas = signal<readonly MyFantasyCompetition[]>([]);
    atual = signal<MyFantasyCompetition | null>(null);
    slugDaRota = signal<string | null>(null);
    trocarCampeonato = vi.fn().mockResolvedValue(undefined);
    organiza = signal(true);
    aberto = signal<OrganizerCompetition | null>(null);

    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([{ path: '**', component: Tela, title: 'Rodadas' }]),
        {
          provide: AuthService,
          useValue: {
            current: conta,
            isDemoViewer: signal(false),
            isPlatformAdmin: computed(() => conta()?.roles.includes('PlatformAdmin') ?? false),
          },
        },
        {
          provide: CurrentCompetition,
          useValue: {
            section: secao,
            mine: minhas,
            current: atual,
            routeSlug: slugDaRota,
            switchTo: trocarCampeonato,
          },
        },
        { provide: OrganizerArea, useValue: { canOrganize: organiza, competition: aberto } },
        {
          provide: NotificationService,
          useValue: {
            naoLidas: signal(0),
            inbox: signal({ unread: 0, items: [] }),
            carregar: vi.fn(),
          },
        },
      ],
    });
  });

  async function abrir(url: string): Promise<ComponentFixture<Shell>> {
    const fixture = TestBed.createComponent(Shell);
    await TestBed.inject(Router).navigateByUrl(url);
    await fixture.whenStable();
    return fixture;
  }

  function nav(fixture: ComponentFixture<Shell>, nome: string): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(`aside nav[aria-label="${nome}"]`);
  }

  function links(elemento: HTMLElement | null): string[] {
    return [...(elemento?.querySelectorAll('a') ?? [])].map((a) => a.textContent!.trim());
  }

  function itens(elemento: Element | null): string[] {
    return [...(elemento?.querySelectorAll('a, button') ?? [])].map((item) =>
      item.textContent!.trim(),
    );
  }

  it('sem campeonato mantém a casca do jogo sem navegação contextual', async () => {
    secao.set('home');
    const fixture = await abrir('/inicio');
    const root = fixture.nativeElement as HTMLElement;
    const app = root.querySelector('.app')!;
    const lateral = root.querySelector('aside')!;
    const inferior = root.querySelector('nav[aria-label="Navegação principal no celular"]');

    expect(app.classList.contains('app--game')).toBe(true);
    expect(lateral.classList.contains('theme-player')).toBe(true);
    expect(nav(fixture, 'Navegação do jogo')).toBeNull();
    expect(lateral.querySelector('select')).toBeNull();
    expect(root.querySelector('.topo__jogo')).toBeNull();
    expect(itens(inferior)).toEqual(['Início', 'Campeonatos', 'Mais']);
  });

  it('com um campeonato mostra rodada, mercado e navegação do jogo', async () => {
    const campeonato = fantasyCompetition();
    minhas.set([campeonato]);
    atual.set(campeonato);
    slugDaRota.set(campeonato.slug);
    secao.set('market');

    const fixture = await abrir('/c/copa-da-vila/mercado');
    const root = fixture.nativeElement as HTMLElement;
    const jogo = nav(fixture, 'Navegação do jogo')!;
    const lateral = root.querySelector('aside')!;
    const inferior = root.querySelector('nav[aria-label="Navegação principal no celular"]');

    expect(links(jogo)).toEqual(['Meu time', 'Mercado', 'Classificação', 'Rodadas']);
    expect(jogo.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Mercado');
    expect(lateral.querySelector('select')).toBeNull();
    expect(lateral.textContent).toContain('Copa da Vila');
    expect(lateral.textContent).toContain('Rodada 3');
    expect(lateral.textContent).toContain('Mercado aberto');
    expect(root.querySelector('.topo__campeonato')?.textContent?.trim()).toBe('Copa da Vila');
    expect(root.querySelector('.topo__rodada')?.textContent?.trim()).toBe('Rodada 3');
    expect(root.querySelector('.topo__jogo-mobile')?.textContent).toContain('Rodada 3');
    expect(root.querySelector('.topo__jogo-mobile')?.textContent).toContain('Mercado aberto');
    expect(root.querySelector('.topo__status')?.getAttribute('aria-label')).toBe(
      'Status do mercado: Mercado aberto',
    );
    expect(itens(inferior)).toEqual(['Início', 'Campeonatos', 'Meu time', 'Mais']);
  });

  it('com vários campeonatos oferece a troca e delega a escolha', async () => {
    const vila = fantasyCompetition();
    const bairro = fantasyCompetition({
      competitionName: 'Copa do Bairro',
      slug: 'copa-do-bairro',
      market: { roundName: 'Rodada 5' },
    });
    minhas.set([vila, bairro]);
    atual.set(vila);
    slugDaRota.set(vila.slug);
    secao.set('market');

    const fixture = await abrir('/c/copa-da-vila/mercado');
    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      'aside select[aria-labelledby="rotulo-campeonato-lateral"]',
    )!;

    expect([...select.options].map((option) => option.textContent?.trim())).toEqual([
      'Copa da Vila',
      'Copa do Bairro',
    ]);
    expect(select.value).toBe('copa-da-vila');

    select.value = 'copa-do-bairro';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(trocarCampeonato).toHaveBeenCalledOnce();
    expect(trocarCampeonato).toHaveBeenCalledWith('copa-do-bairro');
  });

  it('não projeta o campeonato atual em páginas globais', async () => {
    const campeonato = fantasyCompetition();
    minhas.set([campeonato]);
    atual.set(campeonato);
    secao.set('account');

    const fixture = await abrir('/perfil');
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('.topo__campeonato')).toBeNull();
    expect(root.querySelector('.topo__jogo')).toBeNull();
    expect(root.querySelector('.topo__jogo-mobile')).toBeNull();
    expect(root.querySelector('aside')?.textContent).toContain('Copa da Vila');
  });

  it.each([
    { isOpen: true, label: 'Mercado aberto', openClass: true },
    { isOpen: false, label: 'Mercado fechado', openClass: false },
  ])('mostra $label no campeonato atual', async ({ isOpen, label, openClass }) => {
    const campeonato = fantasyCompetition({ market: { isOpen } });
    minhas.set([campeonato]);
    atual.set(campeonato);
    secao.set('home');

    const fixture = await abrir('/inicio');
    const root = fixture.nativeElement as HTMLElement;
    const topoStatus = root.querySelector('.topo__status')!;
    const lateralStatus = root.querySelector('.lateral__mercado')!;

    expect(topoStatus.textContent?.trim()).toBe(label);
    expect(topoStatus.getAttribute('aria-label')).toBe('Status do mercado: ' + label);
    expect(topoStatus.classList.contains('topo__status--aberto')).toBe(openClass);
    expect(lateralStatus.textContent?.trim()).toBe(label);
    expect(lateralStatus.classList.contains('lateral__mercado--aberto')).toBe(openClass);
  });

  it('na organização, a barra lateral troca o jogo pela organização e pelo campeonato aberto', async () => {
    aberto.set(CAMPEONATO);
    const fixture = await abrir('/organizar/c/c1/rodadas');
    const root = fixture.nativeElement as HTMLElement;
    const app = root.querySelector('.app')!;
    const lateral = root.querySelector('aside')!;
    const topo = root.querySelector('.topo')!;

    expect(app.classList.contains('app--organizer')).toBe(true);
    expect(app.classList.contains('app--admin')).toBe(false);
    expect(lateral.classList.contains('theme-organizer')).toBe(true);
    expect(lateral.classList.contains('lateral--admin')).toBe(false);
    expect(topo.classList.contains('topo--organizacao')).toBe(true);
    expect(topo.classList.contains('topo--admin')).toBe(false);
    expect(root.querySelector('.lateral__area')?.textContent?.trim()).toBe('Central da competição');
    expect(root.querySelector('.topo__area')?.textContent?.trim()).toBe('Central da competição');
    expect(links(nav(fixture, 'Organização'))).toEqual(['Minhas organizações']);
    expect(links(nav(fixture, 'Navegação do campeonato'))).toEqual([
      'Resumo',
      'Fases',
      'Rodadas',
      'Times',
      'Atletas',
      'Técnicos',
      'Importações',
      'Publicação',
    ]);
    expect(lateral.textContent).toContain('Copa da Vila');
    expect(lateral.textContent).toContain('Publicado');
    expect(nav(fixture, 'Navegação principal')).toBeNull();

    const volta = nav(fixture, 'Sair da organização')!.querySelector('a')!;
    expect(volta.textContent!.trim()).toBe('Voltar ao jogo');
    expect(volta.getAttribute('href')).toBe('/inicio');
  });

  it('marca a tela aberta, e a súmula de uma partida conta como Rodadas', async () => {
    aberto.set(CAMPEONATO);
    const fixture = await abrir('/organizar/c/c1/partidas/p1/sumula');

    const atual = nav(fixture, 'Navegação do campeonato')!.querySelector('[aria-current="page"]');
    expect(atual?.textContent?.trim()).toBe('Rodadas');
  });

  it('só o proprietário vê a configuração do campeonato', async () => {
    aberto.set({ ...CAMPEONATO, owner: true });
    const fixture = await abrir('/organizar/c/c1');

    expect(links(nav(fixture, 'Navegação do campeonato'))).toContain('Configuração');
    const atual = nav(fixture, 'Navegação do campeonato')!.querySelector('[aria-current="page"]');
    expect(atual?.textContent?.trim()).toBe('Resumo');
  });

  it('a administração da plataforma ganha as solicitações na mesma área', async () => {
    conta.update((atual) => ({ ...atual!, roles: ['PlatformAdmin'] }));
    const fixture = await abrir('/admin/solicitacoes');
    const root = fixture.nativeElement as HTMLElement;
    const app = root.querySelector('.app')!;
    const lateral = root.querySelector('aside')!;
    const topo = root.querySelector('.topo')!;
    const inferior = root.querySelector('.inferior')!;
    const organizacao = nav(fixture, 'Organização')!;

    expect(app.classList.contains('app--admin')).toBe(true);
    expect(app.classList.contains('app--organizer')).toBe(false);
    expect(lateral.classList.contains('theme-organizer')).toBe(true);
    expect(lateral.classList.contains('lateral--admin')).toBe(true);
    expect(topo.classList.contains('topo--admin')).toBe(true);
    expect(inferior.classList.contains('inferior--admin')).toBe(true);
    expect(root.querySelector('.lateral__area')?.textContent?.trim()).toBe('Admin da plataforma');
    expect(root.querySelector('.topo__area')?.textContent?.trim()).toBe('Admin da plataforma');
    expect(links(organizacao)).toEqual(['Minhas organizações', 'Solicitações']);
    expect(organizacao.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe(
      'Solicitações',
    );
  });

  it('quem ainda não organiza continua na casca do jogo, mesmo pedindo acesso', async () => {
    organiza.set(false);
    const fixture = await abrir('/organizar/solicitar');
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('.app')?.classList.contains('app--game')).toBe(true);
    expect(root.querySelector('aside')?.classList.contains('theme-player')).toBe(true);
    expect(nav(fixture, 'Organização')).toBeNull();
    expect(links(nav(fixture, 'Navegação principal'))).toEqual(['Início', 'Campeonatos']);
  });

  it('no celular, a barra inferior da organização leva às organizações e de volta ao início', async () => {
    const fixture = await abrir('/organizar');
    const inferior = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Navegação principal no celular"]',
    );

    expect(links(inferior as HTMLElement)).toEqual(['Organizações', 'Início']);
    expect(inferior?.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe(
      'Organizações',
    );
    expect((fixture.nativeElement as HTMLElement).querySelector('.topo__area')?.textContent).toBe(
      'Organizações',
    );
  });
});
