import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { PublicAthlete, PublicTeamDetail, SquadAthlete } from './public-catalog.service';
import { PublicAthletePage } from './public-athlete';
import { PublicTeamPage } from './public-team';

const SLUG = 'copa-da-vila';
const TIME_ID = '66666666-6666-6666-6666-666666666666';
const ATLETA_ID = '77777777-7777-7777-7777-777777777777';
const TEAM_URL = `/api/v1/public/competitions/${SLUG}/teams/${TIME_ID}`;
const ATHLETE_URL = `/api/v1/public/competitions/${SLUG}/athletes/${ATLETA_ID}`;

function doElenco(changes: Partial<SquadAthlete> = {}): SquadAthlete {
  return {
    id: ATLETA_ID,
    sportingName: 'Pedrinho',
    position: 'Forward',
    price: 8.5,
    active: true,
    ...changes,
  };
}

function time(changes: Partial<PublicTeamDetail> = {}): PublicTeamDetail {
  return {
    id: TIME_ID,
    slug: SLUG,
    competitionName: 'Copa da Vila',
    name: 'União da Vila',
    coachName: 'Técnico do União da Vila',
    athletes: [doElenco()],
    ...changes,
  };
}

function atleta(changes: Partial<PublicAthlete> = {}): PublicAthlete {
  return {
    id: ATLETA_ID,
    slug: SLUG,
    competitionName: 'Copa da Vila',
    sportingName: 'Pedrinho',
    position: 'Forward',
    realTeamId: TIME_ID,
    realTeamName: 'União da Vila',
    price: 9.5,
    active: true,
    totals: {
      matches: 2,
      goals: 3,
      assists: 1,
      goalkeeperSaves: 0,
      penaltySaves: 0,
      yellowCards: 0,
      redCards: 0,
      ownGoals: 0,
      penaltyMisses: 0,
      cleanSheets: 0,
    },
    priceHistory: [
      { roundName: 'Rodada 1', sequence: 1, previousPrice: 8, newPrice: 9.5, variation: 1.5 },
    ],
    ...changes,
  };
}

function texto(fixture: ComponentFixture<unknown>): string {
  return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
}

function configurar(): HttpTestingController {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: API_BASE_URL, useValue: '/api/v1' },
    ],
  });
  return TestBed.inject(HttpTestingController);
}

describe('PublicTeamPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    http = configurar();
  });

  afterEach(() => http.verify());

  async function abrir(dados: PublicTeamDetail): Promise<ComponentFixture<PublicTeamPage>> {
    const fixture = TestBed.createComponent(PublicTeamPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('timeId', TIME_ID);
    await fixture.whenStable();
    http.expectOne(TEAM_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  it('mostra o elenco com posição, preço e o técnico', async () => {
    const fixture = await abrir(time());

    expect(texto(fixture)).toContain('União da Vila');
    expect(texto(fixture)).toContain('Técnico: Técnico do União da Vila');
    expect(texto(fixture)).toContain('Pedrinho');
    expect(texto(fixture)).toContain('Atacante');
    expect(texto(fixture)).toContain('C$ 8,50');
  });

  it('cada atleta leva ao perfil dele', async () => {
    const fixture = await abrir(time());

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/c/${SLUG}/atletas/${ATLETA_ID}"]`,
    );
    expect(link?.textContent).toContain('Pedrinho');
  });

  it('quem saiu do elenco continua listado, marcado', async () => {
    const fixture = await abrir(time({ athletes: [doElenco({ active: false })] }));

    expect(texto(fixture)).toContain('fora do elenco');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.elenco__atleta--fora'),
    ).not.toBeNull();
  });

  it('time sem elenco explica em vez de mostrar lista vazia', async () => {
    const fixture = await abrir(time({ athletes: [] }));

    expect(texto(fixture)).toContain('Nenhum atleta inscrito neste time ainda');
  });

  it('time inexistente não parece erro do sistema', async () => {
    const fixture = TestBed.createComponent(PublicTeamPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('timeId', TIME_ID);
    await fixture.whenStable();
    http.expectOne(TEAM_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este time');
  });
});

describe('PublicAthletePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    http = configurar();
  });

  afterEach(() => http.verify());

  async function abrir(dados: PublicAthlete): Promise<ComponentFixture<PublicAthletePage>> {
    const fixture = TestBed.createComponent(PublicAthletePage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('atletaId', ATLETA_ID);
    await fixture.whenStable();
    http.expectOne(ATHLETE_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  it('mostra posição, time, preço e o que a pessoa fez', async () => {
    const fixture = await abrir(atleta());

    expect(texto(fixture)).toContain('Pedrinho');
    expect(texto(fixture)).toContain('Atacante');
    expect(texto(fixture)).toContain('União da Vila');
    expect(texto(fixture)).toContain('C$ 9,50');
    expect(texto(fixture)).toContain('Jogos');
    expect(texto(fixture)).toContain('Gols');
  });

  it('o perfil não vira uma coluna de zeros', async () => {
    const fixture = await abrir(atleta());

    // Jogos é o denominador e aparece sempre; o que não houve fica de fora.
    expect(texto(fixture)).toContain('Jogos');
    expect(texto(fixture)).not.toContain('Cartões amarelos');
    expect(texto(fixture)).not.toContain('Defesas');
  });

  it('mostra como o preço andou, com o sinal da variação', async () => {
    const fixture = await abrir(atleta());

    expect(texto(fixture)).toContain('Rodada 1');
    expect(texto(fixture)).toContain('C$ 8,00 → C$ 9,50');
    expect(texto(fixture)).toContain('+1,50');
  });

  it('preço que não mudou não vira seta entre números iguais', async () => {
    const fixture = await abrir(
      atleta({
        priceHistory: [
          { roundName: 'Rodada 1', sequence: 1, previousPrice: 9.5, newPrice: 9.5, variation: 0 },
        ],
      }),
    );

    expect(texto(fixture)).toContain('preço mantido em C$ 9,50');
    expect(texto(fixture)).not.toContain('→');
  });

  it('sem rodada apurada, explica os dois vazios', async () => {
    const fixture = await abrir(
      atleta({
        totals: {
          matches: 0,
          goals: 0,
          assists: 0,
          goalkeeperSaves: 0,
          penaltySaves: 0,
          yellowCards: 0,
          redCards: 0,
          ownGoals: 0,
          penaltyMisses: 0,
          cleanSheets: 0,
        },
        priceHistory: [],
      }),
    );

    expect(texto(fixture)).toContain('ainda não entrou em campo numa rodada com resultado');
    expect(texto(fixture)).toContain('Até lá, vale o preço inicial');
  });

  it('leva ao time da pessoa', async () => {
    const fixture = await abrir(atleta());

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/c/${SLUG}/times/${TIME_ID}"]`,
    );
    expect(link?.textContent).toContain('União da Vila');
  });
});
