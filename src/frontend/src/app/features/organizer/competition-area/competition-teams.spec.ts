import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionTeamsPage } from './competition-teams';
import { RealTeam } from './real-team.service';

const TEAMS = '/api/v1/competitions/c1/teams';

function time(parcial: Partial<RealTeam> = {}): RealTeam {
  return {
    id: 't1',
    name: 'União da Vila',
    isArchived: false,
    updatedAt: '2026-09-16T12:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...parcial,
  };
}

async function estabilizar(fixture: ComponentFixture<CompetitionTeamsPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CompetitionTeamsPage', () => {
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
      imports: [CompetitionTeamsPage],
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
    times: RealTeam[],
    papel: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionTeamsPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: papel }));
    const fixture = TestBed.createComponent(CompetitionTeamsPage);
    await fixture.whenStable();
    http.expectOne(TEAMS).flush(times);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionTeamsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(
    fixture: ComponentFixture<CompetitionTeamsPage>,
    rotulo: string,
  ): HTMLButtonElement {
    const encontrado = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.replace(/\s+/g, ' ').trim() === rotulo,
    );
    expect(encontrado, `Botão "${rotulo}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  function preencherNome(fixture: ComponentFixture<CompetitionTeamsPage>, valor: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="text"]',
    ) as HTMLInputElement;
    input.value = valor;
    input.dispatchEvent(new Event('input'));
  }

  it('mostra times ativos e arquivados com escudo de iniciais', async () => {
    const fixture = await abrir([time(), time({ id: 't2', name: 'Estrela FC', isArchived: true })]);

    expect(texto(fixture)).toContain('UV');
    expect(texto(fixture)).toContain('União da Vila');
    expect(texto(fixture)).toContain('EF');
    expect(texto(fixture)).toContain('Estrela FC');
    expect(texto(fixture)).toContain('Arquivado');
    expect(texto(fixture)).not.toContain('Editar Estrela FC');
  });

  it('auxiliar só lê o catálogo', async () => {
    const fixture = await abrir([time()], 'Assistant');

    expect(texto(fixture)).toContain('União da Vila');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button')).toHaveLength(0);
  });

  it('cria um time e recarrega a lista', async () => {
    const fixture = await abrir([]);

    botao(fixture, 'Adicionar time').click();
    await estabilizar(fixture);
    preencherNome(fixture, ' Estrela FC ');
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);

    const pedido = http.expectOne((request) => request.url === TEAMS && request.method === 'POST');
    expect(pedido.request.body).toEqual({ name: 'Estrela FC' });
    pedido.flush(time({ name: 'Estrela FC' }));
    await estabilizar(fixture);
    http.expectOne(TEAMS).flush([time({ name: 'Estrela FC' })]);
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('Estrela FC adicionado.');
  });

  it('mantém o formulário aberto quando o nome já existe', async () => {
    const fixture = await abrir([]);

    botao(fixture, 'Adicionar time').click();
    await estabilizar(fixture);
    preencherNome(fixture, 'Estrela FC');
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);
    http
      .expectOne(TEAMS)
      .flush(
        { title: 'Nome repetido', status: 409, code: 'real_team_name_duplicate' },
        { status: 409, statusText: 'Conflict' },
      );
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('Já existe um time com esse nome');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).not.toBeNull();
  });

  it('envia a versão lida ao editar', async () => {
    const fixture = await abrir([time()]);

    botao(fixture, 'Editar União da Vila').click();
    await estabilizar(fixture);
    preencherNome(fixture, 'União FC');
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);

    const pedido = http.expectOne(`${TEAMS}/t1`);
    expect(pedido.request.method).toBe('PUT');
    expect(pedido.request.body).toEqual({ name: 'União FC', version: 'AAAAAAAAB9E=' });
    pedido.flush(time({ name: 'União FC', version: 'AAAAAAAAB9F=' }));
    await estabilizar(fixture);
    http.expectOne(TEAMS).flush([time({ name: 'União FC', version: 'AAAAAAAAB9F=' })]);
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('União FC salvo.');
  });

  it('arquiva somente depois de confirmar', async () => {
    const fixture = await abrir([time()]);

    botao(fixture, 'Arquivar União da Vila').click();
    await estabilizar(fixture);
    expect(texto(fixture)).toContain('Arquivar União da Vila?');
    botao(fixture, 'Arquivar time').click();
    await estabilizar(fixture);

    const pedido = http.expectOne(`${TEAMS}/t1`);
    expect(pedido.request.method).toBe('DELETE');
    pedido.flush(null, { status: 204, statusText: 'No Content' });
    await estabilizar(fixture);
    http.expectOne(TEAMS).flush([time({ isArchived: true })]);
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('União da Vila arquivado.');
    expect(texto(fixture)).toContain('Arquivado');
  });
});
