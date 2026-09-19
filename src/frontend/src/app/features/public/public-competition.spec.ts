import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { PERFIS } from '../organizer/competition-area/competition-fixtures';
import { PublicCompetitionPage } from './public-competition';
import { PublicCompetition } from './public-competition.service';

const SLUG = 'copa-da-varzea-2026';
const URL = `/api/v1/public/competitions/${SLUG}`;

function campeonato(parcial: Partial<PublicCompetition> = {}): PublicCompetition {
  return {
    slug: SLUG,
    name: 'Copa da Várzea',
    season: '2026',
    modality: 'Fut7',
    organizationName: 'Liga da Várzea',
    timeZoneId: 'America/Sao_Paulo',
    publishedAt: '2026-09-17T12:00:00Z',
    modalityProfile: PERFIS[0],
    stages: [
      {
        name: 'Fase de grupos',
        format: 'Groups',
        sequence: 1,
        groups: [{ name: 'Grupo A', teams: ['Alpha', 'Beta'] }],
        teams: [],
      },
    ],
    teams: [
      { name: 'Alpha', athletes: 6 },
      { name: 'Beta', athletes: 5 },
    ],
    ...parcial,
  };
}

describe('PublicCompetitionPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PublicCompetitionPage],
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

  async function abrir(): Promise<ComponentFixture<PublicCompetitionPage>> {
    const fixture = TestBed.createComponent(PublicCompetitionPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<PublicCompetitionPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('mostra regras da modalidade, fases com grupos e o catálogo', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Copa da Várzea');
    expect(texto(fixture)).toContain('Liga da Várzea');
    expect(texto(fixture)).toContain('1 goleiro, 2 defensores, 2 meio-campistas e 2 atacantes');
    expect(texto(fixture)).toContain('100 créditos');
    expect(texto(fixture)).toContain('Horário de Brasília');
    const jogar = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.jogar',
    );
    expect(jogar?.getAttribute('href')).toBe(`/c/${SLUG}/jogar`);
    expect(texto(fixture)).toContain('1. Fase de grupos');
    expect(texto(fixture)).toContain('Grupo A');
    expect(texto(fixture)).toContain('Alpha, Beta');
    expect(texto(fixture)).toContain('6 atletas inscritos');
  });

  it('mata-mata lista os times sem inventar grupo', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(
      campeonato({
        stages: [
          {
            name: 'Semifinal',
            format: 'Knockout',
            sequence: 1,
            groups: [],
            teams: ['Alpha', 'Beta'],
          },
        ],
      }),
    );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Mata-mata');
    expect(texto(fixture)).toContain('Alpha, Beta');
    expect(texto(fixture)).not.toContain('Grupo');
  });

  it('fase sem times confirmados explica o vazio', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(
      campeonato({
        stages: [{ name: 'Final', format: 'Knockout', sequence: 2, groups: [], teams: [] }],
      }),
    );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Times ainda não confirmados nesta fase.');
  });

  it('endereço inexistente não parece erro do sistema', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato.');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/campeonatos"]'),
    ).not.toBeNull();
  });
});
