import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionDetails } from '../competition.service';
import { CompetitionContext } from './competition-context';
import { campeonato, PERFIS } from './competition-fixtures';
import { CompetitionSettingsPage } from './competition-settings';

const SETTINGS = '/api/v1/competitions/c1/settings';
const PROFILES = '/api/v1/modality-profiles';

async function estabilizar(fixture: ComponentFixture<CompetitionSettingsPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CompetitionSettingsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionSettingsPage],
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

  async function abrirCom(
    dados: CompetitionDetails,
  ): Promise<ComponentFixture<CompetitionSettingsPage>> {
    TestBed.inject(CompetitionContext).replace(dados);
    const fixture = TestBed.createComponent(CompetitionSettingsPage);
    await fixture.whenStable();
    if (dados.viewerRole === 'Owner') {
      http.expectOne(PROFILES).flush(PERFIS);
    }
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionSettingsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function enviar(fixture: ComponentFixture<CompetitionSettingsPage>): void {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
  }

  function botao(fixture: ComponentFixture<CompetitionSettingsPage>, rotulo: string) {
    const encontrado = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.trim() === rotulo,
    );
    expect(encontrado, `Botão "${rotulo}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  it('salva com a versão lida e passa a usar a versão devolvida', async () => {
    const fixture = await abrirCom(campeonato());

    enviar(fixture);
    await estabilizar(fixture);

    const pedido = http.expectOne(SETTINGS);
    expect(pedido.request.method).toBe('PUT');
    expect(pedido.request.body).toMatchObject({
      name: 'Copa da Várzea',
      modality: 'Fut7',
      marketCloseLeadTimeMinutes: 60,
      version: 'AAAAAAAAB9E=',
    });
    pedido.flush(campeonato({ version: 'AAAAAAAAB9I=' }));
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('Configuração salva.');
    expect(TestBed.inject(CompetitionContext).campeonato()?.version).toBe('AAAAAAAAB9I=');
  });

  it('em conflito, não sobrescreve e oferece carregar a versão atual', async () => {
    const fixture = await abrirCom(campeonato());

    enviar(fixture);
    await estabilizar(fixture);
    http
      .expectOne(SETTINGS)
      .flush({ title: 'Conflito', status: 409 }, { status: 409, statusText: 'Conflict' });
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('Alguém salvou esta configuração enquanto você editava.');

    botao(fixture, 'Carregar versão atual').click();
    await estabilizar(fixture);
    const recarga = http.expectOne(SETTINGS);
    expect(recarga.request.method).toBe('GET');
    recarga.flush(campeonato({ name: 'Copa Renomeada', version: 'AAAAAAAAB9M=' }));
    await estabilizar(fixture);

    const nome = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="text"]',
    ) as HTMLInputElement;
    expect(nome.value).toBe('Copa Renomeada');
    expect(texto(fixture)).not.toContain('enquanto você editava');
  });

  it('explica quando a modalidade já está travada pela publicação', async () => {
    const fixture = await abrirCom(campeonato());

    enviar(fixture);
    await estabilizar(fixture);
    http
      .expectOne(SETTINGS)
      .flush(
        { title: 'Modalidade travada', status: 409, code: 'competition_modality_locked' },
        { status: 409, statusText: 'Conflict' },
      );
    await estabilizar(fixture);

    expect(texto(fixture)).toContain('a modalidade não muda mais');
    expect(texto(fixture)).not.toContain('enquanto você editava');
  });

  it('auxiliar vê a explicação e nenhum formulário', async () => {
    const fixture = await abrirCom(campeonato({ viewerRole: 'Assistant' }));

    expect(texto(fixture)).toContain(
      'Só quem é proprietário da organização altera a configuração.',
    );
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });
});
