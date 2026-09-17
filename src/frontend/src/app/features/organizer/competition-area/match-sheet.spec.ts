import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { MatchSheetPage } from './match-sheet';
import { MatchSheet, MatchSheetAthlete } from './match-sheet.service';

const URL = '/api/v1/competitions/c1/matches/m1/sheet';

const ATHLETES: readonly MatchSheetAthlete[] = [
  athlete('a1', 't1', 'Ana', 'Goalkeeper', { playedAsGoalkeeper: true, goalsConceded: 1 }),
  athlete('a2', 't1', 'Bia', 'Forward', { goals: 2 }),
  athlete('a3', 't2', 'Cris', 'Goalkeeper', { playedAsGoalkeeper: true, goalsConceded: 2 }),
  athlete('a4', 't2', 'Dani', 'Forward', { goals: 1 }),
];

function athlete(
  athleteId: string,
  realTeamId: string,
  sportingName: string,
  position: string,
  changes: Partial<MatchSheetAthlete> = {},
): MatchSheetAthlete {
  return {
    athleteId,
    realTeamId,
    sportingName,
    position,
    didPlay: true,
    playedAsGoalkeeper: false,
    goalsConceded: 0,
    goals: 0,
    assists: 0,
    goalkeeperSaves: 0,
    penaltySaves: 0,
    yellowCards: 0,
    redCards: 0,
    redCardReason: null,
    ownGoals: 0,
    penaltyMisses: 0,
    ...changes,
  };
}

function sheet(changes: Partial<MatchSheet> = {}): MatchSheet {
  return {
    matchId: 'm1',
    roundId: 'r1',
    roundName: 'Rodada 1',
    roundPhase: 'InProgress',
    kickoffAt: '2026-09-17T18:00:00Z',
    homeTeamId: 't1',
    homeTeamName: 'Aurora',
    awayTeamId: 't2',
    awayTeamName: 'Estrela',
    homeScore: 2,
    awayScore: 1,
    athletes: ATHLETES,
    version: 'AAAAAAAAB9E=',
    ...changes,
  };
}

describe('MatchSheetPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MatchSheetPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'm1' } } },
        },
        CompetitionContext,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CompetitionContext).replace(campeonato({ id: 'c1', viewerRole: 'Assistant' }));
  });

  afterEach(() => http.verify());

  async function open(value: MatchSheet = sheet()): Promise<ComponentFixture<MatchSheetPage>> {
    const fixture = TestBed.createComponent(MatchSheetPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(value);
    await fixture.whenStable();
    return fixture;
  }

  function text(fixture: ComponentFixture<MatchSheetPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function button(fixture: ComponentFixture<MatchSheetPage>, label: string): HTMLButtonElement {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ].find((item) => item.textContent?.replace(/\s+/g, ' ').trim().startsWith(label))!;
  }

  it('mostra placar, times e as cinco etapas para o auxiliar', async () => {
    const fixture = await open();

    expect(text(fixture)).toContain('Aurora × Estrela');
    expect(text(fixture)).toContain('1. Placar');
    expect(text(fixture)).toContain('5. Revisão');
    expect(
      [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input')].map(
        (item) => item.value,
      ),
    ).toEqual(['2', '1']);
  });

  it('envia a versão lida com participações e eventos depois da revisão', async () => {
    const fixture = await open();
    for (let step = 0; step < 4; step += 1) {
      button(fixture, 'Continuar').click();
      await fixture.whenStable();
    }

    expect(text(fixture)).toContain('4 atleta(s) marcado(s) como participante(s)');
    button(fixture, 'Salvar súmula').click();
    await fixture.whenStable();
    const request = http.expectOne(URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      homeScore: 2,
      awayScore: 1,
      appearances: ATHLETES,
      version: 'AAAAAAAAB9E=',
    });
    request.flush(sheet({ version: 'AAAAAAAAB9I=' }));
    await fixture.whenStable();

    expect(text(fixture)).toContain('Súmula salva para revisão.');
  });

  it('explica quando gols e goleiros não fecham a partida', async () => {
    const fixture = await open(
      sheet({
        homeScore: 1,
        awayScore: 0,
        athletes: ATHLETES.map((item) => ({
          ...item,
          goals: 0,
          playedAsGoalkeeper: false,
          goalsConceded: 0,
        })),
      }),
    );
    for (let step = 0; step < 3; step += 1) {
      button(fixture, 'Continuar').click();
      await fixture.whenStable();
    }

    expect(text(fixture)).toContain('Os gols informados não explicam o placar de Aurora.');
    expect(text(fixture)).toContain('Marque ao menos um goleiro de Aurora.');
    expect(text(fixture)).toContain('Marque ao menos um goleiro de Estrela.');
  });
});
