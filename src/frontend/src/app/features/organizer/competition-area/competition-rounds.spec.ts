import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionRoundsPage } from './competition-rounds';
import { RealTeam } from './real-team.service';
import { Match, Round } from './round.service';
import { Stage } from './stage.service';

const RODADAS = '/api/v1/competitions/c1/rounds';
const FASES = '/api/v1/competitions/c1/stages';
const TIMES = '/api/v1/competitions/c1/teams';

const FASE: Stage = {
  id: 'f1',
  name: 'Fase de grupos',
  format: 'Groups',
  sequence: 1,
  groups: [{ id: 'g1', name: 'Grupo A' }],
  tiebreakers: ['Wins'],
  participants: [
    {
      id: 'p1',
      realTeamId: 't1',
      realTeamName: 'Alpha',
      isTeamArchived: false,
      stageGroupId: 'g1',
      stageGroupName: 'Grupo A',
    },
    {
      id: 'p2',
      realTeamId: 't2',
      realTeamName: 'Beta',
      isTeamArchived: false,
      stageGroupId: 'g1',
      stageGroupName: 'Grupo A',
    },
  ],
  version: 'AAAAAAAAB9E=',
};

const TIMES_ATIVOS: RealTeam[] = [
  { id: 't1', name: 'Alpha', isArchived: false, updatedAt: '2026-09-17T12:00:00Z', version: 'v1' },
  { id: 't2', name: 'Beta', isArchived: false, updatedAt: '2026-09-17T12:00:00Z', version: 'v2' },
];

function partida(parcial: Partial<Match> = {}): Match {
  return {
    id: 'm1',
    stageId: 'f1',
    stageName: 'Fase de grupos',
    homeTeamId: 't1',
    homeTeamName: 'Alpha',
    awayTeamId: 't2',
    awayTeamName: 'Beta',
    groupName: 'Grupo A',
    kickoffAt: '2026-09-20T18:30:00Z',
    kickoffLocal: '2026-09-20T15:30',
    status: 'Scheduled',
    ...parcial,
  };
}

function rodada(parcial: Partial<Round> = {}): Round {
  return {
    id: 'r1',
    name: 'Rodada 1',
    sequence: 1,
    status: 'Draft',
    phase: 'Draft',
    marketCloseAt: null,
    marketCloseLocal: null,
    matches: [],
    version: 'AAAAAAAAB9E=',
    ...parcial,
  };
}

describe('CompetitionRoundsPage', () => {
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
      imports: [CompetitionRoundsPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        CompetitionContext,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(
    rodadas: Round[],
    papel: 'Owner' | 'Assistant' = 'Owner',
    fases: Stage[] = [FASE],
  ): Promise<ComponentFixture<CompetitionRoundsPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: papel }));
    const fixture = TestBed.createComponent(CompetitionRoundsPage);
    await fixture.whenStable();
    http.expectOne(RODADAS).flush(rodadas);
    await fixture.whenStable();
    http.expectOne(FASES).flush(fases);
    await fixture.whenStable();
    http.expectOne(TIMES).flush(TIMES_ATIVOS);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionRoundsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(
    fixture: ComponentFixture<CompetitionRoundsPage>,
    rotulo: string,
  ): HTMLButtonElement | undefined {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ].find((item) => item.textContent?.replace(/\s+/g, ' ').trim().startsWith(rotulo));
  }

  it('mostra a partida com horário do campeonato, fase e grupo', async () => {
    const fixture = await abrir([rodada({ matches: [partida()] })]);

    expect(texto(fixture)).toContain('Alpha × Beta');
    expect(texto(fixture)).toContain('20/09/2026 15:30');
    expect(texto(fixture)).toContain('Fase de grupos');
    expect(texto(fixture)).toContain('Grupo A');
    expect(texto(fixture)).toContain('Rascunho');
  });

  it('mercado aberto mostra o fechamento e troca as ações disponíveis', async () => {
    const fixture = await abrir([
      rodada({
        status: 'MarketOpen',
        phase: 'MarketOpen',
        marketCloseAt: '2026-09-20T17:30:00Z',
        marketCloseLocal: '2026-09-20T14:30',
        matches: [partida()],
      }),
    ]);

    expect(texto(fixture)).toContain('Mercado aberto');
    expect(texto(fixture)).toContain('Mercado fecha em 20/09/2026 14:30');
    expect(botao(fixture, 'Adicionar partida')).toBeUndefined();
    expect(botao(fixture, 'Voltar para rascunho')).toBeDefined();
  });

  it('só oferece times confirmados na fase escolhida', async () => {
    const fixture = await abrir([rodada()]);

    botao(fixture, 'Adicionar partida')?.click();
    await fixture.whenStable();

    const selects = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLSelectElement>('select'),
    ];
    const mandante = selects[1];
    expect([...mandante.options].map((opcao) => opcao.textContent?.trim())).toEqual([
      'Alpha — Grupo A',
      'Beta — Grupo A',
    ]);
  });

  it('envia a partida com o horário local e a versão lida da rodada', async () => {
    const fixture = await abrir([rodada()]);
    botao(fixture, 'Adicionar partida')?.click();
    await fixture.whenStable();

    const selects = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLSelectElement>('select'),
    ];
    selects[1].value = 't1';
    selects[1].dispatchEvent(new Event('change'));
    selects[2].value = 't2';
    selects[2].dispatchEvent(new Event('change'));
    const data = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    data.value = '2026-09-20T15:30';
    data.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const requisicao = http.expectOne(`${RODADAS}/r1/matches`);
    expect(requisicao.request.body).toEqual({
      stageId: 'f1',
      homeTeamId: 't1',
      awayTeamId: 't2',
      kickoffLocal: '2026-09-20T15:30',
      status: 'Scheduled',
      version: 'AAAAAAAAB9E=',
    });
    requisicao.flush(rodada({ matches: [partida()] }));
    await fixture.whenStable();

    http.expectOne(RODADAS).flush([rodada({ matches: [partida()] })]);
    await fixture.whenStable();
    http.expectOne(FASES).flush([FASE]);
    await fixture.whenStable();
    http.expectOne(TIMES).flush(TIMES_ATIVOS);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Partida adicionada.');
  });

  it('erro de validação do servidor aparece com a frase dele', async () => {
    const fixture = await abrir([rodada({ matches: [partida()] })]);

    botao(fixture, 'Abrir mercado')?.click();
    await fixture.whenStable();
    http.expectOne(`${RODADAS}/r1/status`).flush(
      {
        title: 'Dados inválidos',
        errors: { MarketCloseAt: ['O mercado desta rodada já teria fechado.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    http.expectOne(RODADAS).flush([rodada({ matches: [partida()] })]);
    await fixture.whenStable();
    http.expectOne(FASES).flush([FASE]);
    await fixture.whenStable();
    http.expectOne(TIMES).flush(TIMES_ATIVOS);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('já teria fechado');
  });

  it('partida adiada continua visível e marcada como tal', async () => {
    const fixture = await abrir([
      rodada({
        status: 'MarketOpen',
        phase: 'MarketClosed',
        matches: [partida({ status: 'Postponed' })],
      }),
    ]);

    expect(texto(fixture)).toContain('Alpha × Beta');
    expect(texto(fixture)).toContain('Adiada');
    expect(texto(fixture)).toContain('Mercado fechado');
  });

  it('auxiliar lê as rodadas sem receber nenhuma ação', async () => {
    const fixture = await abrir(
      [rodada({ status: 'MarketOpen', phase: 'InProgress', matches: [partida()] })],
      'Assistant',
    );

    expect(texto(fixture)).toContain('Alpha × Beta');
    expect(texto(fixture)).toContain('Revisar rodada');
    expect(botao(fixture, 'Adicionar rodada')).toBeUndefined();
    expect(botao(fixture, 'Adicionar partida')).toBeUndefined();
    expect(botao(fixture, 'Abrir mercado')).toBeUndefined();
  });

  it('sem fase, explica que não dá para marcar jogo ainda', async () => {
    const fixture = await abrir([], 'Owner', []);

    expect(texto(fixture)).toContain('Crie uma fase e confirme os times dela');
  });
});
