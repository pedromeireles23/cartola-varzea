import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { LandingFeatured } from './landing-featured';
import { Ranking, RankingRow } from './public-competition.service';
import { PublicFixture, PublicRound } from './public-fixture.service';

const URL = '/api/v1/public/competitions/copa-da-vila';

function jogo(changes: Partial<PublicFixture> = {}): PublicFixture {
  return {
    id: 'j1',
    stageName: 'Fase única',
    homeTeamName: 'Bar do Nico FC',
    awayTeamName: 'Juventude da Ponte',
    kickoffAt: '2026-09-20T22:00:00Z',
    kickoffLocal: '2026-09-20T19:00:00',
    status: 'Scheduled',
    homeScore: null,
    awayScore: null,
    hasSheet: false,
    ...changes,
  };
}

function rodada(changes: Partial<PublicRound> = {}): PublicRound {
  return {
    id: 'r1',
    name: 'Rodada 1',
    sequence: 1,
    phase: 'Published',
    resultPublished: true,
    underCorrection: false,
    provisional: false,
    matches: [],
    ...changes,
  };
}

function linha(changes: Partial<RankingRow> = {}): RankingRow {
  return {
    position: 1,
    tied: false,
    displayName: 'Bia Lima',
    totalPoints: 85.29,
    netWorth: 100,
    lastRoundPoints: 20,
    isViewer: false,
    ...changes,
  };
}

function ranking(changes: Partial<Ranking> = {}): Ranking {
  return {
    competitionName: 'Copa da Vila',
    rounds: 3,
    lastRoundName: 'Rodada 3',
    provisional: false,
    entries: [],
    ...changes,
  };
}

describe('LandingFeatured', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LandingFeatured],
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
    rodadas: PublicRound[] | 'erro',
    classificacao: Ranking | 'erro',
  ): Promise<ComponentFixture<LandingFeatured>> {
    const fixture = TestBed.createComponent(LandingFeatured);
    fixture.componentRef.setInput('campeonato', {
      slug: 'copa-da-vila',
      name: 'Copa da Vila',
      season: '2026',
      modality: 'Fut7',
      organizationName: 'Liga Várzea do Leste',
      publishedAt: '2026-09-01T12:00:00Z',
    });
    await fixture.whenStable();

    const calendario = http.expectOne(`${URL}/fixtures`);
    if (rodadas === 'erro') {
      calendario.flush(null, { status: 500, statusText: 'Server Error' });
    } else {
      calendario.flush({
        slug: 'copa-da-vila',
        name: 'Copa da Vila',
        timeZoneId: 'America/Sao_Paulo',
        rounds: rodadas,
      });
    }
    const pedidoRanking = http.expectOne(`${URL}/ranking`);
    if (classificacao === 'erro') {
      pedidoRanking.flush(null, { status: 500, statusText: 'Server Error' });
    } else {
      pedidoRanking.flush(classificacao);
    }
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<LandingFeatured>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('apresenta o campeonato e leva a ele, às partidas e ao ranking', async () => {
    const fixture = await abrir([], ranking());
    const elemento = fixture.nativeElement as HTMLElement;

    expect(texto(fixture)).toContain('Fut7 · Temporada 2026 · Liga Várzea do Leste');
    const destinos = [...elemento.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    expect(destinos).toEqual([
      '/c/copa-da-vila',
      '/c/copa-da-vila',
      '/c/copa-da-vila/partidas',
      '/c/copa-da-vila/ranking',
    ]);
  });

  it('mostra todas as rodadas em andamento, não só a primeira', async () => {
    const fixture = await abrir(
      [
        rodada({ id: 'r3', name: 'Rodada 3' }),
        rodada({ id: 'r4', name: 'Rodada 4', phase: 'UnderReview', resultPublished: false }),
        rodada({ id: 'r5', name: 'Rodada 5', phase: 'MarketOpen', resultPublished: false }),
      ],
      ranking(),
    );

    const fases = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.fase')].map(
      (item) => item.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(fases).toEqual(['Rodada 4 · Em conferência', 'Rodada 5 · Mercado aberto']);
  });

  it('o último resultado traz só partidas com placar, avisando se é provisório', async () => {
    const fixture = await abrir(
      [
        rodada({
          name: 'Rodada 3',
          provisional: true,
          matches: [
            jogo({ id: 'a', homeScore: 5, awayScore: 1 }),
            jogo({ id: 'b', homeTeamName: 'Adiado FC', status: 'Postponed' }),
          ],
        }),
      ],
      ranking(),
    );

    expect(texto(fixture)).toContain('Último resultado · Rodada 3');
    expect(texto(fixture)).toContain('Provisório: pode mudar até a rodada consolidar');
    const placares = (fixture.nativeElement as HTMLElement).querySelectorAll('.placar');
    expect(placares).toHaveLength(1);
    expect(placares[0].textContent?.replace(/\s+/g, ' ')).toContain('Bar do Nico FC 5×1');
    // O placar desenhado sai da árvore e o leitor de tela ouve "5 a 1", com espaço
    // inseparável nas bordas para o número não colar no nome do time.
    expect(placares[0].querySelector('.placar__numeros')?.getAttribute('aria-hidden')).toBe('true');
    expect(placares[0].querySelector('.sr-only')?.textContent).toBe('\u00a05 a 1\u00a0');
  });

  it('sem resultado publicado, mostra os próximos jogos no horário do campeonato', async () => {
    const fixture = await abrir(
      [
        rodada({
          phase: 'MarketOpen',
          resultPublished: false,
          matches: [
            jogo({ kickoffAt: '2999-09-25T23:00:00Z', kickoffLocal: '2999-09-25T20:00:00' }),
          ],
        }),
      ],
      ranking(),
    );

    expect(texto(fixture)).toContain('Próximos jogos');
    expect(texto(fixture)).toContain('25/09 · 20:00');
  });

  it('sem partida marcada, diz isso em vez de deixar o painel vazio', async () => {
    const fixture = await abrir([], ranking());

    expect(texto(fixture)).toContain('Nenhuma partida marcada ainda');
  });

  it('o ranking mostra os três primeiros, até quando pode mudar e o empate por extenso', async () => {
    const fixture = await abrir(
      [],
      ranking({
        provisional: true,
        entries: [
          linha(),
          linha({ position: 2, tied: true, displayName: 'Caio Mendes', totalPoints: 83.38 }),
          linha({ position: 2, tied: true, displayName: 'Lia Campos', totalPoints: 83.38 }),
          linha({ position: 4, displayName: 'Quarto Lugar', totalPoints: 10 }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('Depois da Rodada 3 · provisório');
    expect(texto(fixture)).toContain('Bia Lima');
    expect(texto(fixture)).toContain('85,29 pts');
    expect(texto(fixture)).toContain('2º , empatado');
    expect(texto(fixture)).not.toContain('Quarto Lugar');
  });

  it('antes da primeira apuração, o ranking diz que ninguém pontuou', async () => {
    const fixture = await abrir([], ranking({ lastRoundName: null, entries: [] }));

    expect(texto(fixture)).toContain('Ninguém pontuou ainda');
  });

  it('se calendário e ranking falharem, o cartaz continua de pé', async () => {
    const fixture = await abrir('erro', 'erro');

    expect(texto(fixture)).toContain('Copa da Vila');
    expect(texto(fixture)).toContain('O calendário não carregou agora');
    expect(texto(fixture)).toContain('O ranking não carregou agora');
  });
});
