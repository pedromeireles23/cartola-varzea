import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionStagesPage } from './competition-stages';
import { Stage } from './stage.service';

const STAGES = '/api/v1/competitions/c1/stages';

function fase(parcial: Partial<Stage>): Stage {
  return {
    id: 's1',
    name: 'Fase de grupos',
    format: 'Groups',
    sequence: 1,
    groups: [
      { id: 'g1', name: 'Grupo A' },
      { id: 'g2', name: 'Grupo B' },
    ],
    tiebreakers: ['Wins', 'GoalDifference'],
    version: 'AAAAAAAAB9E=',
    ...parcial,
  };
}

const GRUPOS = fase({});
const MATA_MATA = fase({
  id: 's2',
  name: 'Mata-mata',
  format: 'Knockout',
  sequence: 2,
  groups: [],
  tiebreakers: [],
});

async function estabilizar(fixture: ComponentFixture<CompetitionStagesPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CompetitionStagesPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    // O jsdom não implementa o modal nativo; foco e Esc ficam com o E2E.
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
      imports: [CompetitionStagesPage],
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
    fases: Stage[],
    papel: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionStagesPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: papel }));
    const fixture = TestBed.createComponent(CompetitionStagesPage);
    await fixture.whenStable();
    http.expectOne(STAGES).flush(fases);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionStagesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(fixture: ComponentFixture<CompetitionStagesPage>, rotulo: string) {
    const encontrado = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.replace(/\s+/g, ' ').trim() === rotulo,
    );
    expect(encontrado, `Botão "${rotulo}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  it('mostra as fases em ordem com grupos e desempate em texto', async () => {
    const fixture = await abrir([GRUPOS, MATA_MATA]);

    expect(texto(fixture)).toContain('Não existe sequência obrigatória');
    expect(texto(fixture)).toContain('1. Fase de grupos');
    expect(texto(fixture)).toContain('Grupo A, Grupo B');
    expect(texto(fixture)).toContain('Mais vitórias');
    expect(texto(fixture)).toContain('Maior saldo de gols');
    expect(texto(fixture)).toContain('2. Mata-mata');
    expect(botao(fixture, 'Subir Fase de grupos').disabled).toBe(true);
    expect(botao(fixture, 'Descer Mata-mata').disabled).toBe(true);
  });

  it('auxiliar só lê: nenhuma ação de escrita aparece', async () => {
    const fixture = await abrir([GRUPOS], 'Assistant');

    expect(texto(fixture)).toContain('1. Fase de grupos');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button')).toHaveLength(0);
  });

  it('descer uma fase envia a nova ordem completa e mostra o resultado', async () => {
    const fixture = await abrir([GRUPOS, MATA_MATA]);

    botao(fixture, 'Descer Fase de grupos').click();
    await estabilizar(fixture);

    const pedido = http.expectOne(`${STAGES}/order`);
    expect(pedido.request.body).toEqual({ stageIds: ['s2', 's1'] });
    pedido.flush([
      { ...MATA_MATA, sequence: 1 },
      { ...GRUPOS, sequence: 2 },
    ]);
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('Fase de grupos agora é a fase 2.');
    expect(texto(fixture)).toContain('1. Mata-mata');
  });

  it('adiciona uma fase e recarrega a lista pela API', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Nenhuma fase ainda.');
    botao(fixture, 'Adicionar fase').click();
    await estabilizar(fixture);
    const nome = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="text"]',
    ) as HTMLInputElement;
    nome.value = 'Primeira fase';
    nome.dispatchEvent(new Event('input'));
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);

    const pedido = http.expectOne((request) => request.method === 'POST' && request.url === STAGES);
    expect(pedido.request.body).toMatchObject({ name: 'Primeira fase', format: 'Groups' });
    pedido.flush(fase({ name: 'Primeira fase' }));
    await estabilizar(fixture);

    http.expectOne(STAGES).flush([fase({ name: 'Primeira fase' })]);
    await estabilizar(fixture);
    expect(texto(fixture)).toContain('Primeira fase adicionada.');
    expect(texto(fixture)).toContain('1. Primeira fase');
  });

  it('conflito ao salvar recarrega a lista e pede para refazer a mudança', async () => {
    const fixture = await abrir([GRUPOS]);

    botao(fixture, 'Editar Fase de grupos').click();
    await estabilizar(fixture);
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);

    const pedido = http.expectOne(`${STAGES}/s1`);
    expect(pedido.request.body).toMatchObject({ version: 'AAAAAAAAB9E=' });
    pedido.flush({ title: 'Conflito', status: 409 }, { status: 409, statusText: 'Conflict' });
    await estabilizar(fixture);

    http.expectOne(STAGES).flush([fase({ name: 'Grupos renomeados' })]);
    await estabilizar(fixture);
    expect(texto(fixture)).toContain('As fases foram alteradas por outra pessoa');
    expect(texto(fixture)).toContain('1. Grupos renomeados');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });

  it('remove depois de confirmar', async () => {
    const fixture = await abrir([GRUPOS, MATA_MATA]);

    botao(fixture, 'Remover Mata-mata').click();
    await estabilizar(fixture);
    expect(texto(fixture)).toContain('Remover Mata-mata?');
    botao(fixture, 'Remover fase').click();
    await estabilizar(fixture);

    const pedido = http.expectOne(`${STAGES}/s2`);
    expect(pedido.request.method).toBe('DELETE');
    pedido.flush(null, { status: 204, statusText: 'No Content' });
    await estabilizar(fixture);

    http.expectOne(STAGES).flush([GRUPOS]);
    await estabilizar(fixture);
    expect(texto(fixture)).toContain('Mata-mata removida.');
    expect(texto(fixture)).not.toContain('2. Mata-mata');
  });
});
