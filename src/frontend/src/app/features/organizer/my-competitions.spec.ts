import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { MyCompetition } from './competition.service';
import { MyCompetitionsPage } from './my-competitions';
import { MyOrganization } from './organization.service';

const ORGANIZACOES = '/api/v1/organizations/mine';
const CAMPEONATOS = '/api/v1/organizations/mine/competitions';

function campeonato(parcial: Partial<MyCompetition>): MyCompetition {
  return {
    id: 'c1',
    name: 'Copa da Vila',
    season: '2026',
    modality: 'Fut7',
    status: 'Published',
    updatedAt: '2026-10-14T12:00:00Z',
    organizationId: 'o1',
    organizationName: 'Liga Própria',
    viewerRole: 'Owner',
    activeTeams: 6,
    matches: 12,
    currentRound: null,
    nextRound: null,
    ...parcial,
  };
}

describe('MyCompetitionsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyCompetitionsPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrirCom(
    organizacoes: MyOrganization[],
    campeonatos: MyCompetition[],
  ): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(MyCompetitionsPage);
    await fixture.whenStable();
    http.expectOne(ORGANIZACOES).flush(organizacoes);
    http.expectOne(CAMPEONATOS).flush(campeonatos);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const propria: MyOrganization = {
    id: 'o1',
    name: 'Liga Própria',
    role: 'Owner',
    joinedAt: '2026-09-16T12:00:00Z',
  };
  const ajudada: MyOrganization = {
    id: 'o2',
    name: 'Liga Ajudada',
    role: 'Assistant',
    joinedAt: '2026-09-16T12:00:00Z',
  };

  it('o campeonato vem primeiro, agrupado pela organização, com o próximo passo', async () => {
    const elemento = await abrirCom(
      [propria, ajudada],
      [
        campeonato({ id: 'c1', name: 'Copa da Vila' }),
        campeonato({
          id: 'c2',
          name: 'Taça Ajudada',
          organizationId: 'o2',
          organizationName: 'Liga Ajudada',
          viewerRole: 'Assistant',
          status: 'Draft',
        }),
      ],
    );

    const secoes = [...elemento.querySelectorAll('section.org')];
    expect(secoes.map((secao) => secao.querySelector('h2')?.textContent?.trim())).toEqual([
      'Liga Própria',
      'Liga Ajudada',
    ]);

    // Um clique leva ao campeonato, e o atalho do próximo passo diz em qual.
    const copa = secoes[0].querySelector('li.campeonato')!;
    expect(copa.querySelector('h3 a')?.getAttribute('href')).toBe('/organizar/c/c1');
    expect(copa.textContent).toContain('Fut7 · temporada 2026 · 6 times · 12 jogos');
    expect(copa.querySelector('a.acao')?.textContent?.trim()).toBe(
      'Ir para a tabela em Copa da Vila',
    );

    // Só o proprietário cria campeonato e cuida da equipe.
    const destinos = (secao: Element) =>
      [...secao.querySelectorAll('.org__acoes a')].map((link) => link.getAttribute('href'));
    expect(destinos(secoes[0])).toEqual([
      '/organizar/o/o1/campeonatos/novo',
      '/organizar/o/o1/equipe',
    ]);
    expect(destinos(secoes[1])).toEqual([]);
    expect(secoes[1].textContent).toContain('Auxiliar');
    expect(secoes[1].textContent).toContain('Em montagem pela organização.');
  });

  it('o que pede súmula vem antes e se destaca; o rascunho fica por último', async () => {
    const elemento = await abrirCom(
      [propria],
      [
        campeonato({
          id: 'c3',
          name: 'Copa Rascunho',
          status: 'Draft',
          activeTeams: 0,
          matches: 0,
        }),
        campeonato({ id: 'c4', name: 'Copa Tranquila' }),
        campeonato({
          id: 'c5',
          name: 'Copa em Conferência',
          currentRound: {
            id: 'r4',
            name: 'Rodada 4',
            phase: 'UnderReview',
            matches: 3,
            firstKickoffLocal: '2026-10-11T09:00',
            marketCloseLocal: '2026-10-11T08:00',
          },
        }),
      ],
    );

    const cartoes = [...elemento.querySelectorAll('li.campeonato')];
    expect(cartoes.map((cartao) => cartao.querySelector('h3')?.textContent?.trim())).toEqual([
      'Copa em Conferência',
      'Copa Tranquila',
      'Copa Rascunho',
    ]);
    expect(cartoes[0].classList.contains('campeonato--urgente')).toBe(true);
    expect(cartoes[0].querySelector('a.acao')?.getAttribute('href')).toBe(
      '/organizar/c/c5/rodadas/r4/revisao',
    );
    expect(cartoes[2].querySelector('a.acao')?.getAttribute('href')).toBe('/organizar/c/c3/montar');
  });

  it('organização sem campeonato convida o proprietário a criar o primeiro', async () => {
    const elemento = await abrirCom([propria], []);

    expect(elemento.textContent).toContain('Nenhum campeonato ainda. Crie o primeiro');
  });

  it('sem organização, explica como pedir acesso ou aceitar convite', async () => {
    const elemento = await abrirCom([], []);

    expect(elemento.textContent).toContain('Nenhuma organização ainda');
    expect(elemento.textContent).toContain('convite para auxiliar');
    expect(elemento.querySelector('a[href="/organizar/solicitar"]')).not.toBeNull();
  });
});
