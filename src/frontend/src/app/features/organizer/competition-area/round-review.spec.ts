import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { RoundReviewPage } from './round-review';
import { RoundPublication, RoundReview } from './round.service';

const URL = '/api/v1/competitions/c1/rounds/r1/review';
const STATUS_URL = '/api/v1/competitions/c1/rounds/r1/status';
const PUBLISH_URL = '/api/v1/competitions/c1/rounds/r1/publish';
const REOPEN_URL = '/api/v1/competitions/c1/rounds/r1/reopen';

function publication(changes: Partial<RoundPublication> = {}): RoundPublication {
  return {
    revision: 1,
    scoringRuleSetVersion: 1,
    publishedAt: '2026-09-21T15:00:00Z',
    publishedAtLocal: '2026-09-21T12:00',
    consolidatesAt: '2026-09-23T13:00:00Z',
    consolidatesAtLocal: '2026-09-23T10:00',
    consolidated: false,
    entries: 12,
    highestTotal: 35,
    averageTotal: 18.25,
    correctionReason: null,
    ...changes,
  };
}

/** Rodada publicada e consolidada, o estado de onde a correção parte. */
function consolidada(changes: Partial<RoundReview> = {}): RoundReview {
  return review({
    phase: 'Consolidated',
    publication: publication({ consolidated: true }),
    ...changes,
  });
}

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
    publication: null,
    correction: null,
    ...changes,
  };
}

describe('RoundReviewPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    const prototipo = HTMLDialogElement.prototype as HTMLDialogElement & {
      showModal?: () => void;
    };
    prototipo.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototipo.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
  });

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

  it('importa as súmulas da rodada pela planilha e atualiza a conferência sem sumir o resultado', async () => {
    const fixture = await open(review({ completedSheets: 0, ready: false }), 'Assistant');
    const element = fixture.nativeElement as HTMLElement;

    const link = [...element.querySelectorAll('a')].find(
      (item) => item.textContent?.trim() === 'Baixar planilha da rodada',
    );
    expect(link?.getAttribute('href')).toBe(
      '/api/v1/competitions/c1/rounds/r1/imports/estatisticas/template',
    );

    const arquivo = new File(['mandante;visitante\r\n'], 'rodada.csv', { type: 'text/csv' });
    const entrada = element.querySelector('input[type=file]')!;
    Object.defineProperty(entrada, 'files', { configurable: true, value: [arquivo] });
    entrada.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    const resultado = {
      outcome: 'Completed',
      summary: { rows: 4, created: 1, updated: 0, unchanged: 0 },
      issues: [],
      rejectionMessage: null,
      notes: ['Aurora 2 x 1 Estrela: súmula nova.'],
    };
    button(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    http
      .expectOne('/api/v1/competitions/c1/rounds/r1/imports/estatisticas/preview')
      .flush(resultado);
    await fixture.whenStable();
    expect(text(fixture)).toContain(
      '4 linhas lidas: 1 súmula nova, 0 a substituir, 0 sem mudança.',
    );
    expect(text(fixture)).toContain('Aurora 2 x 1 Estrela: súmula nova.');

    button(fixture, 'Importar')?.click();
    await fixture.whenStable();
    http.expectOne('/api/v1/competitions/c1/rounds/r1/imports/estatisticas').flush(resultado);
    await fixture.whenStable();
    http.expectOne(URL).flush(review());
    await fixture.whenStable();

    expect(text(fixture)).toContain('Importado');
    expect(text(fixture)).toContain('1 de 1');
  });

  it('não oferece a planilha antes do início dos jogos', async () => {
    const fixture = await open(review({ phase: 'MarketOpen' }));

    expect(text(fixture)).not.toContain('Súmulas por planilha');
  });

  it('o proprietário publica a rodada em revisão depois de confirmar', async () => {
    const fixture = await open(review({ phase: 'UnderReview' }));

    button(fixture, 'Publicar resultado')!.click();
    await fixture.whenStable();
    const dialog = (fixture.nativeElement as HTMLElement).querySelector('dialog')!;
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(dialog.textContent).toContain('Publicar o resultado de Rodada 1?');

    [...dialog.querySelectorAll('button')]
      .find((item) => item.textContent?.trim() === 'Publicar')!
      .click();
    await fixture.whenStable();
    const request = http.expectOne(PUBLISH_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    request.flush(review({ phase: 'Published', publication: publication() }));
    await fixture.whenStable();

    expect(text(fixture)).toContain('Resultado publicado. Os pontos e os preços novos já valem');
    expect(text(fixture)).toContain('Resultado provisório até 23/09/2026 10:00');
    expect(text(fixture)).toMatch(/Participações apuradas\s*12/);
    expect(text(fixture)).toMatch(/Maior pontuação\s*35,00 pts/);
    expect(text(fixture)).toMatch(/Média\s*18,25 pts/);
    expect(button(fixture, 'Publicar resultado')).toBeUndefined();
  });

  it('o auxiliar acompanha a revisão, mas não publica', async () => {
    const fixture = await open(review({ phase: 'UnderReview' }), 'Assistant');

    expect(text(fixture)).toContain('Só quem é proprietário do campeonato publica o resultado.');
    expect(button(fixture, 'Publicar resultado')).toBeUndefined();
  });

  it('diz quando o resultado já consolidou', async () => {
    const fixture = await open(consolidada());

    expect(text(fixture)).toContain('Resultado consolidado desde 23/09/2026 10:00.');
    expect(text(fixture)).toContain('Consolidada');
  });

  it('o proprietário reabre a rodada consolidada informando o motivo', async () => {
    const fixture = await open(consolidada());

    button(fixture, 'Reabrir para correção')!.click();
    await fixture.whenStable();
    const dialog = dialogoAberto(fixture);
    expect(dialog.textContent).toContain('Reabrir Rodada 1 para correção?');
    expect(dialog.textContent).toContain('Esta rodada já consolidou');

    escrever(dialog, 'Gol lançado no atleta errado pela arbitragem.');
    acionar(dialog, 'Reabrir');
    await fixture.whenStable();

    const request = http.expectOne(REOPEN_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      version: 'AAAAAAAAB9E=',
      reason: 'Gol lançado no atleta errado pela arbitragem.',
    });
    request.flush(
      consolidada({
        phase: 'ReopenedForCorrection',
        correction: {
          reopenedAt: '2026-09-24T13:00:00Z',
          reopenedAtLocal: '2026-09-24T10:00',
          reason: 'Gol lançado no atleta errado pela arbitragem.',
        },
      }),
    );
    await fixture.whenStable();

    expect(text(fixture)).toContain('Rodada reaberta. Corrija a súmula e republique');
    expect(text(fixture)).toContain('Rodada reaberta em 24/09/2026 10:00');
    expect(text(fixture)).toContain('Quem joga continua vendo a 1ª apuração');
    expect(text(fixture)).toContain('Republicar resultado');
    expect(text(fixture)).toContain('Súmulas por planilha');
    expect(button(fixture, 'Reabrir para correção')).toBeUndefined();
  });

  it('não envia a reabertura de uma rodada consolidada sem motivo', async () => {
    const fixture = await open(consolidada());

    button(fixture, 'Reabrir para correção')!.click();
    await fixture.whenStable();
    acionar(dialogoAberto(fixture), 'Reabrir');
    await fixture.whenStable();

    http.expectNone(REOPEN_URL);
    expect(text(fixture)).toContain('Explique a correção em pelo menos 10 caracteres.');
  });

  it('a rodada ainda provisória é reaberta sem motivo', async () => {
    const fixture = await open(review({ phase: 'Published', publication: publication() }));

    button(fixture, 'Reabrir para correção')!.click();
    await fixture.whenStable();
    const dialog = dialogoAberto(fixture);
    expect(dialog.textContent).toContain(
      'A rodada ainda está provisória, então o motivo é opcional.',
    );

    acionar(dialog, 'Reabrir');
    await fixture.whenStable();

    const request = http.expectOne(REOPEN_URL);
    expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=', reason: null });
    request.flush(
      review({
        phase: 'ReopenedForCorrection',
        publication: publication(),
        correction: {
          reopenedAt: '2026-09-22T13:00:00Z',
          reopenedAtLocal: '2026-09-22T10:00',
          reason: null,
        },
      }),
    );
    await fixture.whenStable();

    expect(text(fixture)).toContain('Rodada reaberta em 22/09/2026 10:00');
  });

  it('republicar avisa que as rodadas seguintes são refeitas junto', async () => {
    const fixture = await open(
      consolidada({
        phase: 'ReopenedForCorrection',
        correction: {
          reopenedAt: '2026-09-24T13:00:00Z',
          reopenedAtLocal: '2026-09-24T10:00',
          reason: 'Placar conferido errado.',
        },
      }),
    );

    expect(text(fixture)).toContain('refaz na mesma hora toda rodada seguinte que já saiu');
    button(fixture, 'Republicar resultado')!.click();
    await fixture.whenStable();
    const dialog = dialogoAberto(fixture);
    expect(dialog.textContent).toContain('Republicar o resultado de Rodada 1?');
    expect(dialog.textContent).toContain('nas seguintes que já saíram é refeita de uma vez');

    acionar(dialog, 'Republicar');
    await fixture.whenStable();
    http.expectOne(PUBLISH_URL).flush(
      review({
        phase: 'Published',
        publication: publication({ revision: 2, correctionReason: 'Placar conferido errado.' }),
      }),
    );
    await fixture.whenStable();

    expect(text(fixture)).toContain('Resultado corrigido. As rodadas seguintes que já saíram');
    expect(text(fixture)).toMatch(/Revisão da apuração\s*2ª/);
    expect(text(fixture)).toMatch(/Motivo da correção\s*Placar conferido errado\./);
  });

  it('o auxiliar não reabre a rodada', async () => {
    const fixture = await open(consolidada(), 'Assistant');

    expect(button(fixture, 'Reabrir para correção')).toBeUndefined();
  });

  function dialogoAberto(fixture: ComponentFixture<RoundReviewPage>): HTMLDialogElement {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLDialogElement>('dialog'),
    ].find((item) => item.hasAttribute('open'))!;
  }

  function escrever(dialog: HTMLDialogElement, texto: string): void {
    const campo = dialog.querySelector('textarea')!;
    campo.value = texto;
    campo.dispatchEvent(new Event('input'));
  }

  function acionar(dialog: HTMLDialogElement, label: string): void {
    [...dialog.querySelectorAll('button')]
      .find((item) => item.textContent?.trim() === label)!
      .click();
  }
});
