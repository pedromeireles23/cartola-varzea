import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import {
  MERCADO_FECHADO,
  OVERVIEW_URL,
  ROUNDS_URL,
  SLUG,
  entrada,
  mercadoAberto,
  rodadaResumo,
  vaga,
  visao,
} from '../fantasy/fantasy-fixtures';
import { MyFantasyCompetition } from '../fantasy/fantasy.service';
import { calendario, jogo, rodada } from '../public/fixtures-fixtures';
import { Ranking, RankingRow } from '../public/public-competition.service';
import { ParticipantHomePage } from './participant-home';

const MINHAS_URL = '/api/v1/fantasy';
const RANKING_URL = `/api/v1/public/competitions/${SLUG}/ranking`;
const FIXTURES_URL = `/api/v1/public/competitions/${SLUG}/fixtures`;

function minha(parcial: Partial<MyFantasyCompetition> = {}): MyFantasyCompetition {
  return {
    competitionName: 'Copa da Várzea',
    slug: SLUG,
    season: '2026',
    modality: 'Fut7',
    balance: 100,
    squadSize: 0,
    squadSizeTarget: 12,
    hasCaptain: false,
    joinedAt: '2026-09-20T12:00:00Z',
    market: mercadoAberto(),
    ...parcial,
  };
}

function linha(parcial: Partial<RankingRow> = {}): RankingRow {
  return {
    position: 1,
    tied: false,
    displayName: 'Tropa do Bar',
    totalPoints: 184.2,
    netWorth: 110,
    lastRoundPoints: 40,
    isViewer: false,
    ...parcial,
  };
}

function ranking(parcial: Partial<Ranking> = {}): Ranking {
  return {
    competitionName: 'Copa da Várzea',
    rounds: 3,
    lastRoundName: 'Rodada 3',
    provisional: false,
    entries: [
      linha(),
      linha({ position: 2, displayName: 'Real Madruga', totalPoints: 179.8 }),
      linha({ position: 3, displayName: 'Os Canela', totalPoints: 170.1 }),
      linha({ position: 4, displayName: 'Pedro Meireles', totalPoints: 151.6, isViewer: true }),
    ],
    ...parcial,
  };
}

describe('ParticipantHomePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    globalThis.localStorage?.clear();
    TestBed.configureTestingModule({
      imports: [ParticipantHomePage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        {
          provide: AuthService,
          useValue: {
            current: signal({
              id: 'user-id',
              email: 'pedro@exemplo.local',
              displayName: 'Pedro Meireles',
              emailConfirmed: true,
              roles: [],
            }).asReadonly(),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(): Promise<ComponentFixture<ParticipantHomePage>> {
    const fixture = TestBed.createComponent(ParticipantHomePage);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<ParticipantHomePage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  async function comCampeonato(
    fixture: ComponentFixture<ParticipantHomePage>,
    opcoes: { visao?: Parameters<typeof visao>[0]; ranking?: Ranking } = {},
  ): Promise<void> {
    http.expectOne(MINHAS_URL).flush([minha()]);
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(visao(opcoes.visao));
    http.expectOne(RANKING_URL).flush(opcoes.ranking ?? ranking({ rounds: 0, entries: [] }));
    http.expectOne(FIXTURES_URL).flush(
      calendario({
        rounds: [
          rodada({
            matches: [
              jogo({ kickoffAt: '2099-09-24T23:00:00Z', kickoffLocal: '2099-09-24T20:00' }),
            ],
          }),
        ],
      }),
    );
    http.expectOne(ROUNDS_URL).flush([]);
    await fixture.whenStable();
  }

  it('recebe a conta nova com o próximo passo, a busca e os campeonatos abertos', async () => {
    const fixture = await abrir();
    http.expectOne(MINHAS_URL).flush([]);
    await fixture.whenStable();
    http.expectOne('/api/v1/public/competitions').flush([
      {
        slug: 'copa-norte-2026',
        name: 'Copa Norte',
        season: '2026',
        modality: 'Fut7',
        organizationName: 'Liga do Bairro',
        publishedAt: '2026-09-20T12:00:00Z',
      },
    ]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Sua conta está pronta. Agora escolha onde quer jogar.');
    expect(texto(fixture)).toContain('Você ainda não participa de um campeonato');
    expect(texto(fixture)).toContain('Explorar campeonatos');
    expect(texto(fixture)).toContain('Copa Norte');
    expect(texto(fixture)).toContain('Recebeu convite para uma liga?');
    expect(texto(fixture)).not.toContain('Montar time');
  });

  it('abre com o prazo absoluto do mercado e a ação de montar o time', async () => {
    const fixture = await abrir();
    await comCampeonato(fixture);

    expect(texto(fixture)).toContain('Mercado da Rodada 1 fecha dom., 20/09 às 19:00');
    expect(texto(fixture)).toContain('Montar time');
    expect(texto(fixture)).toContain('0 de 12 escolhidos');
    expect(texto(fixture)).toContain('Falta 1 goleiro entre os titulares.');
    expect(texto(fixture)).toContain('Orçamento disponível C$ 100,00');
    expect(texto(fixture)).toContain('A classificação começa depois da primeira rodada apurada');
    expect(texto(fixture)).toContain('Estrela do Bairro qui., 24/09 · 20:00');
  });

  it('com o elenco pela metade, o convite vira continuar a escalação', async () => {
    const fixture = await abrir();
    await comCampeonato(fixture, {
      visao: { entry: entrada({ balance: 71, slots: [vaga(), vaga({ name: 'Rafa' })] }) },
    });

    expect(texto(fixture)).toContain('Continuar escalação');
    expect(texto(fixture)).toContain('2 de 12 escolhidos');
    expect(texto(fixture)).toContain('Orçamento disponível C$ 71,00');
  });

  it('com o mercado fechado, diz o estado e não oferece escalar', async () => {
    const fixture = await abrir();
    await comCampeonato(fixture, { visao: { market: MERCADO_FECHADO } });

    expect(texto(fixture)).toContain('Mercado fechado');
    expect(texto(fixture)).not.toContain('Montar time');
    expect(texto(fixture)).toContain('Ver meu time');
  });

  it('mostra a posição, os pontos, o líder e a linha de quem lê', async () => {
    const fixture = await abrir();
    await comCampeonato(fixture, { ranking: ranking() });

    expect(texto(fixture)).toContain('4º de 4');
    expect(texto(fixture)).toMatch(/Seus pontos\s*151,60 pts/);
    expect(texto(fixture)).toMatch(/Líder\s*184,20 pts/);
    const minhaLinha = (fixture.nativeElement as HTMLElement).querySelector('.mini-tabela__voce');
    expect(minhaLinha?.textContent).toContain('4. Pedro Meireles');
    expect(texto(fixture)).not.toContain('Os Canela');
  });

  it('avisa quando a última rodada apurada ainda é provisória', async () => {
    const fixture = await abrir();
    http.expectOne(MINHAS_URL).flush([minha()]);
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(visao());
    http.expectOne(RANKING_URL).flush(ranking());
    http.expectOne(FIXTURES_URL).flush(calendario({ rounds: [] }));
    http.expectOne(ROUNDS_URL).flush([rodadaResumo({ total: 38.4 })]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('A Rodada 1 foi apurada: 38,40 pts');
    expect(texto(fixture)).toContain('provisório até qua., 23/09 às 10:00');
  });
});
