import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CompetitionFixturesPage } from './competition-fixtures';
import { FIXTURES_URL, PARTIDA_ID, SLUG, calendario, jogo, rodada } from './fixtures-fixtures';
import { PublicFixtures } from './public-fixture.service';

describe('CompetitionFixturesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionFixturesPage],
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

  async function abrir(dados: PublicFixtures): Promise<ComponentFixture<CompetitionFixturesPage>> {
    const fixture = TestBed.createComponent(CompetitionFixturesPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(FIXTURES_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionFixturesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('mostra a rodada, o horário e a fase de cada jogo', async () => {
    const fixture = await abrir(calendario());

    expect(texto(fixture)).toContain('Partidas de Copa da Vila');
    expect(texto(fixture)).toContain('Rodada 1');
    expect(texto(fixture)).toContain('Mercado aberto');
    expect(texto(fixture)).toContain('União da Vila');
    expect(texto(fixture)).toContain('Estrela do Bairro');
    expect(texto(fixture)).toContain('20/09/2026 10:00 · Fase única');
  });

  it('sem resultado publicado não há placar nem link para a súmula', async () => {
    const fixture = await abrir(calendario());

    expect(texto(fixture)).not.toContain('Ver súmula');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector(
        `a[href="/c/${SLUG}/partidas/${PARTIDA_ID}"]`,
      ),
    ).toBeNull();
  });

  it('rodada publicada mostra o placar e leva à súmula', async () => {
    const fixture = await abrir(
      calendario({
        rounds: [
          rodada({
            phase: 'Published',
            resultPublished: true,
            provisional: true,
            matches: [jogo({ homeScore: 2, awayScore: 0, hasSheet: true })],
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('Resultado provisório');
    expect(texto(fixture)).toContain('2');
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/c/${SLUG}/partidas/${PARTIDA_ID}"]`,
    );
    expect(link?.textContent).toContain('Ver súmula');
  });

  it('rodada em correção explica por que o placar sumiu', async () => {
    const fixture = await abrir(
      calendario({
        rounds: [
          rodada({
            phase: 'ReopenedForCorrection',
            underCorrection: true,
            resultPublished: false,
            matches: [jogo()],
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('Em correção');
    expect(texto(fixture)).toContain('A liga está refazendo a súmula');
    expect(texto(fixture)).not.toContain('Ver súmula');
  });

  it('rodada consolidada não fica mais marcada como provisória', async () => {
    const fixture = await abrir(
      calendario({
        rounds: [rodada({ phase: 'Consolidated', resultPublished: true, provisional: false })],
      }),
    );

    expect(texto(fixture)).toContain('Resultado final');
    expect(texto(fixture)).not.toContain('Resultado provisório');
  });

  it('partida adiada continua no calendário, dizendo que não aconteceu', async () => {
    const fixture = await abrir(
      calendario({ rounds: [rodada({ matches: [jogo({ status: 'Postponed' })] })] }),
    );

    expect(texto(fixture)).toContain('adiada');
    expect((fixture.nativeElement as HTMLElement).querySelector('.jogo--fora')).not.toBeNull();
  });

  it('campeonato sem rodada explica em vez de mostrar lista vazia', async () => {
    const fixture = await abrir(calendario({ rounds: [] }));

    expect(texto(fixture)).toContain('Nenhuma rodada foi criada ainda');
  });

  it('não confunde campeonato inexistente com erro de rede', async () => {
    const fixture = TestBed.createComponent(CompetitionFixturesPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(FIXTURES_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato');
  });
});
