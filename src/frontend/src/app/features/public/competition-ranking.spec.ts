import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CompetitionRankingPage } from './competition-ranking';
import { Ranking, RankingRow } from './public-competition.service';

const SLUG = 'copa-da-vila';
const URL = `/api/v1/public/competitions/${SLUG}/ranking`;

function linha(changes: Partial<RankingRow> = {}): RankingRow {
  return {
    position: 1,
    tied: false,
    displayName: 'Pessoa Um',
    totalPoints: 46,
    netWorth: 104.5,
    lastRoundPoints: 16,
    isViewer: false,
    ...changes,
  };
}

function ranking(changes: Partial<Ranking> = {}): Ranking {
  return {
    competitionName: 'Copa da Vila',
    rounds: 2,
    lastRoundName: 'Rodada 2',
    provisional: false,
    entries: [linha()],
    ...changes,
  };
}

describe('CompetitionRankingPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionRankingPage],
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

  async function abrir(dados: Ranking): Promise<ComponentFixture<CompetitionRankingPage>> {
    const fixture = TestBed.createComponent(CompetitionRankingPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionRankingPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('mostra colocação, pontos, patrimônio e o que a pessoa fez na última rodada', async () => {
    const fixture = await abrir(ranking());

    expect(texto(fixture)).toContain('Classificação');
    expect(texto(fixture)).toContain('Copa da Vila');
    expect(texto(fixture)).toContain('2 rodadas apuradas, até Rodada 2.');
    expect(texto(fixture)).toContain('1º');
    expect(texto(fixture)).toContain('Pessoa Um');
    expect(texto(fixture)).toContain('46,00 pts');
    expect(texto(fixture)).toContain('C$ 104,50');
    expect(texto(fixture)).toContain('16,00 pts na última');
    // É uma tabela: cabeçalho de coluna e o nome como cabeçalho da linha.
    const tabela = (fixture.nativeElement as HTMLElement).querySelector('table');
    expect(tabela?.querySelectorAll('thead th')).toHaveLength(5);
    expect(tabela?.querySelector('tbody th[scope="row"]')?.textContent).toContain('Pessoa Um');
  });

  it('avisa que a classificação pode mudar enquanto a última rodada é provisória', async () => {
    const fixture = await abrir(ranking({ provisional: true }));

    expect(texto(fixture)).toContain('Rodada 2 ainda é provisória');
    expect(texto(fixture)).not.toContain('rodadas apuradas, até');
  });

  it('marca quem divide a colocação e quem não jogou a última rodada', async () => {
    const fixture = await abrir(
      ranking({
        entries: [
          linha({ position: 1, tied: true, displayName: 'Empatada A' }),
          linha({ position: 1, tied: true, displayName: 'Empatado B' }),
          linha({ position: 3, displayName: 'Ausente', totalPoints: 10, lastRoundPoints: null }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('Empatada A');
    expect(texto(fixture)).toContain('empatado');
    expect(texto(fixture)).toContain('3º');
    expect(texto(fixture)).toContain('não jogou a última');
  });

  it('quem está logado se reconhece na lista', async () => {
    const fixture = await abrir(ranking({ entries: [linha({ isViewer: true })] }));

    expect(texto(fixture)).toContain('Você');
    expect((fixture.nativeElement as HTMLElement).querySelector('.linha--voce')).not.toBeNull();
  });

  it('campeonato sem rodada apurada explica que a classificação ainda não se mexeu', async () => {
    const fixture = await abrir(ranking({ rounds: 0, lastRoundName: null }));

    expect(texto(fixture)).toContain('Nenhuma rodada foi apurada ainda');
    expect(texto(fixture)).not.toContain('na última');
  });

  it('campeonato sem participantes convida a entrar', async () => {
    const fixture = await abrir(ranking({ rounds: 0, lastRoundName: null, entries: [] }));

    expect(texto(fixture)).toContain('Ninguém entrou neste campeonato ainda');
    const convite = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.acao',
    );
    expect(convite?.getAttribute('href')).toBe(`/c/${SLUG}/jogar`);
  });

  it('não confunde campeonato inexistente com erro de rede', async () => {
    const fixture = TestBed.createComponent(CompetitionRankingPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato');
  });
});
