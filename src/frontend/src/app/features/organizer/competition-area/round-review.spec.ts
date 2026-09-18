import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { RoundReviewPage } from './round-review';
import { RoundReview } from './round.service';

const URL = '/api/v1/competitions/c1/rounds/r1/review';
const STATUS_URL = '/api/v1/competitions/c1/rounds/r1/status';

function review(changes: Partial<RoundReview> = {}): RoundReview {
  return {
    roundId: 'r1',
    roundName: 'Rodada 1',
    phase: 'InProgress',
    scheduledMatches: 1,
    completedSheets: 1,
    ready: true,
    matches: [
      {
        matchId: 'm1',
        homeTeamName: 'Aurora',
        awayTeamName: 'Estrela',
        kickoffLocal: '2026-09-17T15:00',
        status: 'Scheduled',
        requiresSheet: true,
        hasSheet: true,
        homeScore: 2,
        awayScore: 1,
        participants: 4,
        events: [{ type: 'Goal', quantity: 3 }],
      },
    ],
    pending: [],
    version: 'AAAAAAAAB9E=',
    ...changes,
  };
}

describe('RoundReviewPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RoundReviewPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'r1' } } } },
        CompetitionContext,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(
    value: RoundReview,
    role: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<RoundReviewPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ id: 'c1', viewerRole: role }));
    const fixture = TestBed.createComponent(RoundReviewPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(value);
    await fixture.whenStable();
    return fixture;
  }

  function text(fixture: ComponentFixture<RoundReviewPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function button(
    fixture: ComponentFixture<RoundReviewPage>,
    label: string,
  ): HTMLButtonElement | undefined {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ].find((item) => item.textContent?.replace(/\s+/g, ' ').trim().startsWith(label));
  }

  it('mostra placar e eventos consolidados para o auxiliar sem permitir a transição', async () => {
    const fixture = await open(review(), 'Assistant');

    expect(text(fixture)).toContain('Aurora × Estrela');
    expect(text(fixture)).toContain('2 × 1');
    expect(text(fixture)).toContain('3 Gols');
    expect(button(fixture, 'Enviar para revisão')).toBeUndefined();
  });

  it('lista todas as pendências e mantém o envio desabilitado', async () => {
    const fixture = await open(
      review({
        completedSheets: 0,
        ready: false,
        matches: [{ ...review().matches[0], hasSheet: false, homeScore: null, awayScore: null }],
        pending: [
          {
            code: 'sheet_missing',
            message: 'Preencha a súmula de Aurora × Estrela.',
            matchId: 'm1',
          },
          {
            code: 'match_not_started',
            message: 'Aurora × Estrela ainda não começou.',
            matchId: 'm1',
          },
        ],
      }),
    );

    expect(text(fixture)).toContain('Preencha a súmula de Aurora × Estrela.');
    expect(text(fixture)).toContain('Aurora × Estrela ainda não começou.');
    expect(button(fixture, 'Enviar para revisão')?.disabled).toBe(true);
  });

  it('envia a versão lida e recarrega a rodada em revisão', async () => {
    const fixture = await open(review());

    button(fixture, 'Enviar para revisão')?.click();
    await fixture.whenStable();
    const request = http.expectOne(STATUS_URL);
    expect(request.request.body).toEqual({
      transition: 'SendToReview',
      version: 'AAAAAAAAB9E=',
    });
    request.flush({});
    await fixture.whenStable();
    http.expectOne(URL).flush(review({ phase: 'UnderReview', version: 'AAAAAAAAB9I=' }));
    await fixture.whenStable();

    expect(text(fixture)).toContain('Em apuração');
    expect(button(fixture, 'Enviar para revisão')).toBeUndefined();
  });
});
