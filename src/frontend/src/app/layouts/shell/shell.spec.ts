import { Component, computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { Account, AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/notifications/notification.service';
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

describe('Shell', () => {
  let conta: ReturnType<typeof signal<Account | null>>;
  let secao: ReturnType<typeof signal<Section | null>>;
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
            mine: signal([]),
            current: signal(null),
            routeSlug: signal(null),
            switchTo: vi.fn(),
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

  it('na organização, a barra lateral troca o jogo pela organização e pelo campeonato aberto', async () => {
    aberto.set(CAMPEONATO);
    const fixture = await abrir('/organizar/c/c1/rodadas');
    const lateral = (fixture.nativeElement as HTMLElement).querySelector('aside')!;

    expect(lateral.textContent).toContain('Organização');
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

    const organizacao = nav(fixture, 'Organização')!;
    expect(links(organizacao)).toEqual(['Minhas organizações', 'Solicitações']);
    expect(organizacao.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe(
      'Solicitações',
    );
  });

  it('quem ainda não organiza continua na casca do jogo, mesmo pedindo acesso', async () => {
    organiza.set(false);
    const fixture = await abrir('/organizar/solicitar');

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
      'Organização',
    );
  });
});
