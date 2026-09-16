import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { campeonato, PERFIS } from './competition-area/competition-fixtures';
import { CreateCompetitionPage } from './create-competition';
import { MyOrganization } from './organization.service';

const ORGANIZATION = '/api/v1/organizations/o1';
const PROFILES = '/api/v1/modality-profiles';
const COMPETITIONS = '/api/v1/organizations/o1/competitions';

async function estabilizar(fixture: ComponentFixture<CreateCompetitionPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('CreateCompetitionPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CreateCompetitionPage],
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
    papel: MyOrganization['role'],
  ): Promise<ComponentFixture<CreateCompetitionPage>> {
    const fixture = TestBed.createComponent(CreateCompetitionPage);
    fixture.componentRef.setInput('organizacao', 'o1');
    await fixture.whenStable();
    http
      .expectOne(ORGANIZATION)
      .flush({ id: 'o1', name: 'Liga da Várzea', role: papel, joinedAt: '2026-09-16T12:00:00Z' });
    http.expectOne(PROFILES).flush(PERFIS);
    await fixture.whenStable();
    return fixture;
  }

  function preencher(fixture: ComponentFixture<CreateCompetitionPage>): void {
    const elemento = fixture.nativeElement as HTMLElement;
    const nome = elemento.querySelector('input[type="text"]') as HTMLInputElement;
    nome.value = 'Copa Nova';
    nome.dispatchEvent(new Event('input'));
    const futsal = elemento.querySelector('input[value="Futsal"]') as HTMLInputElement;
    futsal.checked = true;
    futsal.dispatchEvent(new Event('change'));
  }

  it('cria em rascunho e abre o resumo avisando da criação', async () => {
    const fixture = await abrir('Owner');
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    preencher(fixture);
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);

    const pedido = http.expectOne(COMPETITIONS);
    expect(pedido.request.method).toBe('POST');
    expect(pedido.request.body).toMatchObject({
      name: 'Copa Nova',
      modality: 'Futsal',
      timeZoneId: 'America/Sao_Paulo',
      resultsSlaBusinessDays: 2,
      correctionWindowBusinessDays: 3,
    });
    pedido.flush(campeonato({ id: 'c9', name: 'Copa Nova', modality: 'Futsal' }));
    await estabilizar(fixture);

    expect(navegar).toHaveBeenCalledWith(['/organizar/c', 'c9'], { state: { criado: true } });
  });

  it('mostra a falha da API sem sair da tela', async () => {
    const fixture = await abrir('Owner');

    preencher(fixture);
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await estabilizar(fixture);
    http
      .expectOne(COMPETITIONS)
      .flush({ title: 'Erro', status: 500 }, { status: 500, statusText: 'Server Error' });
    await estabilizar(fixture);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Algo deu errado do nosso lado.',
    );
  });

  it('auxiliar recebe a explicação no lugar do formulário', async () => {
    const fixture = await abrir('Assistant');
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Só quem é proprietário de Liga da Várzea cria');
    expect(elemento.querySelector('form')).toBeNull();
  });
});
