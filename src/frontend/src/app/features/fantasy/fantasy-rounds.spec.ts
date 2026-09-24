import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { jogo, rodada } from '../public/fixtures-fixtures';
import { PublicRound } from '../public/public-fixture.service';
import {
  MERCADO_FECHADO,
  OVERVIEW_URL,
  ROUNDS_URL,
  SLUG,
  entrada,
  rodadaResumo,
  vaga,
  visao,
} from './fantasy-fixtures';
import { FantasyRoundsPage } from './fantasy-rounds';
import { FantasyOverview, FantasyRoundSummary } from './fantasy.service';

const FIXTURES_URL = `/api/v1/public/competitions/${SLUG}/fixtures`;

describe('FantasyRoundsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyRoundsPage],
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

  async function abrir(
    dados: FantasyOverview,
    rodadas: FantasyRoundSummary[] = [],
    calendario: PublicRound[] = [],
  ): Promise<ComponentFixture<FantasyRoundsPage>> {
    const fixture = TestBed.createComponent(FantasyRoundsPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(dados);
    http.expectOne(FIXTURES_URL).flush({
      slug: SLUG,
      name: 'Copa da Várzea',
      timeZoneId: 'America/Sao_Paulo',
      rounds: calendario,
    });
    await fixture.whenStable();
    if (dados.entry) {
      http.expectOne(ROUNDS_URL).flush(rodadas);
      await fixture.whenStable();
    }
    return fixture;
  }

  function texto(fixture: ComponentFixture<FantasyRoundsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function link(fixture: ComponentFixture<FantasyRoundsPage>, nome: string): HTMLAnchorElement {
    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('a')];
    const achado = links.find((item) => item.textContent?.replace(/\s+/g, ' ').trim() === nome);
    expect(achado, `link "${nome}"`).toBeDefined();
    return achado!;
  }

  it('mostra as partidas da rodada em jogo, com a situação de cada uma', async () => {
    const fixture = await abrir(
      visao(),
      [],
      [
        rodada({ name: 'Rodada 1', sequence: 1, phase: 'Published', matches: [jogo()] }),
        rodada({
          id: 'r2',
          name: 'Rodada 2',
          sequence: 2,
          phase: 'MarketOpen',
          matches: [
            jogo({ id: 'j1', kickoffLocal: '2026-09-27T10:00' }),
            jogo({ id: 'j2', homeTeamName: 'Real Mangueiral', status: 'Postponed' }),
          ],
        }),
      ],
    );

    expect(texto(fixture)).toContain('Partidas da Rodada 2');
    expect(texto(fixture)).toContain('dom., 27/09 · 10:00');
    expect(texto(fixture)).toContain('Real Mangueiral');
    expect(texto(fixture)).toContain('Adiada');
    expect(texto(fixture)).not.toContain('Partidas da Rodada 1');
  });

  it('a ação acompanha a escalação: montar, continuar ou só ver', async () => {
    const vazia = await abrir(visao({ entry: entrada() }));
    expect(link(vazia, 'Montar time').getAttribute('href')).toBe(`/c/${SLUG}/escalacao`);
    vazia.destroy();

    const pelaMetade = await abrir(visao({ entry: entrada({ slots: [vaga()] }) }));
    expect(link(pelaMetade, 'Continuar escalação').className).not.toContain('secundaria');
    pelaMetade.destroy();

    const completa = await abrir(visao({ entry: entrada({ slots: [vaga()], issues: [] }) }));
    expect(link(completa, 'Ver escalação').className).toContain('acao--secundaria');
  });

  it('com o mercado fechado não oferece ação nenhuma sobre o elenco', async () => {
    const fixture = await abrir(visao({ market: MERCADO_FECHADO, entry: entrada() }));

    expect(texto(fixture)).toContain('Mercado fechado');
    expect(texto(fixture)).not.toContain('Montar time');
    expect(texto(fixture)).not.toContain('Continuar escalação');
  });

  it('lista as rodadas apuradas com o total e diz até quando cada uma pode mudar', async () => {
    const fixture = await abrir(visao(), [
      rodadaResumo({ roundId: 'r2', roundName: 'Rodada 2', total: 12.5 }),
      rodadaResumo({
        roundId: 'r1',
        roundName: 'Rodada 1',
        total: 20,
        provisional: false,
      }),
    ]);

    expect(texto(fixture)).toContain('Total no campeonato 32,50 pts');
    expect(texto(fixture)).toContain('Provisória');
    expect(texto(fixture)).toContain(
      'Publicada seg., 21/09 · 12:00 · pode mudar até qua., 23/09 · 10:00',
    );
    expect(texto(fixture)).toContain('consolidada');
    // A linha inteira abre a pontuação detalhada da rodada.
    const linha = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/c/${SLUG}/pontuacao/r1"]`,
    );
    expect(linha?.textContent).toContain('20,00 pts');
  });

  it('marca a rodada em correção e a rodada em que a conta não jogou', async () => {
    const fixture = await abrir(visao(), [
      rodadaResumo({ roundId: 'r2', roundName: 'Rodada 2', underCorrection: true }),
      rodadaResumo({ roundId: 'r1', roundName: 'Rodada 1', total: null, provisional: false }),
    ]);

    expect(texto(fixture)).toContain('Em correção');
    expect(texto(fixture)).toContain('a liga está corrigindo a súmula');
    expect(texto(fixture)).toContain('Não jogou');
  });

  it('quem ainda não entrou no campeonato é convidado a entrar, sem pedir a pontuação', async () => {
    const fixture = await abrir(visao({ entry: null }));

    expect(texto(fixture)).toContain(
      'A pontuação de cada rodada aparece aqui depois que você entra',
    );
    expect(link(fixture, 'Entrar no campeonato').getAttribute('href')).toBe(`/c/${SLUG}/jogar`);
  });

  it('sem calendário a tela continua de pé, só sem as partidas', async () => {
    const fixture = TestBed.createComponent(FantasyRoundsPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(visao());
    http.expectOne(FIXTURES_URL).flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    http.expectOne(ROUNDS_URL).flush([]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Mercado aberto');
    expect(texto(fixture)).not.toContain('Partidas da');
  });
});
