import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { PERFIS } from '../organizer/competition-area/competition-fixtures';
import { PublicCompetitionPage } from './public-competition';
import { PublicCompetition } from './public-competition.service';
import { PublicFixture, PublicRound } from './public-fixture.service';

const SLUG = 'copa-da-varzea-2026';
const URL = `/api/v1/public/competitions/${SLUG}`;
const FIXTURES = `${URL}/fixtures`;

/** Daqui a tantas horas, no formato que o servidor devolve. */
function daqui(horas: number): string {
  return new Date(Date.now() + horas * 3_600_000).toISOString();
}

function jogo(changes: Partial<PublicFixture> = {}): PublicFixture {
  return {
    id: 'jogo-1',
    stageName: 'Fase única',
    homeTeamName: 'Alpha',
    awayTeamName: 'Beta',
    kickoffAt: daqui(24),
    kickoffLocal: '2026-09-24T10:00',
    status: 'Scheduled',
    homeScore: null,
    awayScore: null,
    hasSheet: false,
    ...changes,
  };
}

function rodada(changes: Partial<PublicRound> = {}): PublicRound {
  return {
    id: 'rodada-1',
    name: 'Rodada 1',
    sequence: 1,
    phase: 'MarketOpen',
    resultPublished: false,
    underCorrection: false,
    provisional: false,
    matches: [jogo()],
    ...changes,
  };
}

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
      { id: 'time-alpha', name: 'Alpha', athletes: 6 },
      { id: 'time-beta', name: 'Beta', athletes: 5 },
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

  /**
   * O calendário é acessório nesta página, então ele é respondido por padrão com nada;
   * quem testa "agora" e "próximos jogos" passa as rodadas.
   */
  async function abrir(
    rodadas: PublicRound[] = [],
  ): Promise<ComponentFixture<PublicCompetitionPage>> {
    const fixture = TestBed.createComponent(PublicCompetitionPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(FIXTURES).flush({
      slug: SLUG,
      name: 'Copa da Várzea',
      timeZoneId: 'America/Sao_Paulo',
      rounds: rodadas,
    });
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

  it('campeonato publicado e ainda vazio explica que está em montagem', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(campeonato({ stages: [], teams: [] }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Campeonato ainda em montagem');
    expect(texto(fixture)).toContain('ainda não cadastrou os times nem as fases');
    // Um aviso só, em vez de três "nada aqui" separados.
    expect(texto(fixture)).not.toContain('As fases ainda não foram publicadas');
    expect(texto(fixture)).not.toContain('O catálogo de times ainda não foi cadastrado');
  });

  it('sem catálogo, não oferece jogar: não haveria atleta para comprar', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(campeonato({ teams: [] }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('O catálogo de times ainda não foi cadastrado');
    expect((fixture.nativeElement as HTMLElement).querySelector('a.jogar')).toBeNull();
    // O ranking continua acessível: ele explica sozinho que nada se mexeu ainda.
    expect(
      (fixture.nativeElement as HTMLElement).querySelector(`a[href="/c/${SLUG}/ranking"]`),
    ).not.toBeNull();
  });

  it('com catálogo, o convite para jogar aparece', async () => {
    const fixture = await abrir();
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    const jogar = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.jogar',
    );
    expect(jogar?.getAttribute('href')).toBe(`/c/${SLUG}/jogar`);
  });

  it('diz em que pé a rodada está e o que vem por aí', async () => {
    const fixture = await abrir([rodada()]);
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Agora');
    expect(texto(fixture)).toContain('Rodada 1');
    expect(texto(fixture)).toContain('Mercado aberto');
    expect(texto(fixture)).toContain('Próximos jogos');
    expect(texto(fixture)).toContain('Alpha');
    expect(texto(fixture)).toContain('qui., 24/09 · 10:00 · Fase única');
  });

  it('com uma rodada em conferência e o mercado da próxima aberto, mostra as duas', async () => {
    const fixture = await abrir([
      rodada({ id: 'r4', name: 'Rodada 4', sequence: 4, phase: 'UnderReview', matches: [] }),
      rodada({ id: 'r5', name: 'Rodada 5', sequence: 5, phase: 'MarketOpen' }),
    ]);
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    // Mostrar só a primeira escondia justamente a rodada que ainda dá para jogar.
    expect(texto(fixture)).toContain('Rodada 4 Em conferência');
    expect(texto(fixture)).toContain('Rodada 5 Mercado aberto');
  });

  it('rodada em correção avisa que os números vão mudar', async () => {
    const fixture = await abrir([
      rodada({ phase: 'ReopenedForCorrection', underCorrection: true, matches: [] }),
    ]);
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Em correção');
    expect(texto(fixture)).toContain('Os números voltam quando ela republicar');
  });

  it('sem jogo futuro, mostra o último resultado no lugar', async () => {
    const fixture = await abrir([
      rodada({
        phase: 'Consolidated',
        resultPublished: true,
        matches: [jogo({ kickoffAt: daqui(-48), homeScore: 3, awayScore: 1, hasSheet: true })],
      }),
    ]);
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Último resultado · Rodada 1');
    expect(texto(fixture)).toContain('3');
    expect(texto(fixture)).not.toContain('Próximos jogos');
    // Todas as rodadas fechadas: não há "agora" a mostrar.
    expect(texto(fixture)).not.toContain('Agora');
  });

  it('calendário fora do ar não derruba a página do campeonato', async () => {
    const fixture = TestBed.createComponent(PublicCompetitionPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(FIXTURES).flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne(URL).flush(campeonato());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Copa da Várzea');
    expect(texto(fixture)).toContain('Como se joga');
    expect(texto(fixture)).not.toContain('Próximos jogos');
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
