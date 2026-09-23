import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CompetitionStandingsPage } from './competition-standings';
import { PublicStandings, StageStandings, StandingsRow } from './public-fixture.service';

const SLUG = 'copa-da-vila';
const URL = `/api/v1/public/competitions/${SLUG}/standings`;

function linha(changes: Partial<StandingsRow> = {}): StandingsRow {
  return {
    position: 1,
    tied: false,
    teamId: 'time-alfa',
    teamName: 'União da Vila',
    played: 2,
    wins: 2,
    draws: 0,
    losses: 0,
    goalsFor: 5,
    goalsAgainst: 1,
    goalDifference: 4,
    points: 6,
    ...changes,
  };
}

function fase(changes: Partial<StageStandings> = {}): StageStandings {
  return {
    name: 'Fase única',
    sequence: 1,
    format: 'Groups',
    tiebreakers: ['GoalDifference', 'GoalsFor'],
    groups: [{ name: 'Grupo A', rows: [linha()] }],
    ...changes,
  };
}

function tabela(changes: Partial<PublicStandings> = {}): PublicStandings {
  return { slug: SLUG, name: 'Copa da Vila', stages: [fase()], ...changes };
}

describe('CompetitionStandingsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionStandingsPage],
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
    dados: PublicStandings,
  ): Promise<ComponentFixture<CompetitionStandingsPage>> {
    const fixture = TestBed.createComponent(CompetitionStandingsPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionStandingsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('mostra a campanha completa de cada time', async () => {
    const fixture = await abrir(tabela());

    expect(texto(fixture)).toContain('Tabela de Copa da Vila');
    expect(texto(fixture)).toContain('Grupo A');
    expect(texto(fixture)).toContain('União da Vila');
    // Pontos, jogos, vitórias, empates, derrotas e saldo.
    expect(texto(fixture)).toContain('6');
    expect(texto(fixture)).toContain('+4');
  });

  it('usa uma tabela de verdade, com cabeçalho de linha e de coluna', async () => {
    const fixture = await abrir(tabela());
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.querySelector('table')).not.toBeNull();
    expect(elemento.querySelector('caption')?.textContent).toContain('Classificação');
    expect(elemento.querySelector('thead th[scope="col"]')).not.toBeNull();
    // O nome do time é o cabeçalho da linha: é por ele que a célula faz sentido.
    expect(elemento.querySelector('tbody th[scope="row"]')?.textContent).toContain('União da Vila');
  });

  it('conta por que um time está na frente do outro', async () => {
    const fixture = await abrir(tabela());

    expect(texto(fixture)).toContain(
      'Empate em pontos é decidido por saldo de gols, depois gols marcados.',
    );
  });

  it('sem critério nenhum, diz que o empate não é desfeito', async () => {
    const fixture = await abrir(tabela({ stages: [fase({ tiebreakers: [] })] }));

    expect(texto(fixture)).toContain('Empate em pontos não é desfeito nesta fase');
  });

  it('empate aparece escrito, não só pela colocação repetida', async () => {
    const fixture = await abrir(
      tabela({
        stages: [
          fase({
            groups: [
              {
                name: 'Grupo A',
                rows: [
                  linha({ position: 1, tied: true }),
                  linha({ position: 1, tied: true, teamId: 'time-beta', teamName: 'Estrela' }),
                ],
              },
            ],
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('empatado');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.tabela__empate')).toHaveLength(
      2,
    );
  });

  it('mata-mata explica que não tem classificação por pontos', async () => {
    const fixture = await abrir(
      tabela({ stages: [fase({ name: 'Final', format: 'Knockout', groups: [] })] }),
    );

    expect(texto(fixture)).toContain('Esta fase é de mata-mata');
    expect(texto(fixture)).toContain('não tem classificação por pontos');
    expect((fixture.nativeElement as HTMLElement).querySelector('table')).toBeNull();
    // A navegação também aponta para as partidas; aqui interessa o convite do cartão.
    const confrontos = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a.acao[href="/c/${SLUG}/partidas"]`,
    );
    expect(confrontos?.textContent).toContain('Ver os confrontos');
  });

  it('cada time leva ao elenco dele', async () => {
    const fixture = await abrir(tabela());

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a[href="/c/${SLUG}/times/time-alfa"]`,
    );
    expect(link?.textContent).toContain('União da Vila');
  });

  it('campeonato sem fase explica em vez de mostrar tabela vazia', async () => {
    const fixture = await abrir(tabela({ stages: [] }));

    expect(texto(fixture)).toContain('ainda não tem fases publicadas');
  });

  it('fase sem time confirmado explica o vazio', async () => {
    const fixture = await abrir(tabela({ stages: [fase({ groups: [] })] }));

    expect(texto(fixture)).toContain('Nenhum time confirmado nesta fase ainda');
  });

  it('diz que só resultado publicado conta', async () => {
    const fixture = await abrir(tabela());

    expect(texto(fixture)).toContain('Só entra resultado já publicado');
    expect(texto(fixture)).toContain('Partida adiada ou cancelada fica de fora');
  });

  it('não confunde campeonato inexistente com erro de rede', async () => {
    const fixture = TestBed.createComponent(CompetitionStandingsPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato');
  });
});
