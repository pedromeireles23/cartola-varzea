import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizerApplication } from './organizer-application.service';
import { RequestAccessPage } from './request-access';

const MINE = '/api/v1/organizer-applications/mine';
const SUBMIT = '/api/v1/organizer-applications';

function solicitacao(parcial: Partial<OrganizerApplication>): OrganizerApplication {
  return {
    id: 'a1',
    organizationName: 'Liga da Várzea',
    status: 'Pending',
    submittedAt: '2026-09-16T12:30:00+00:00',
    decidedAt: null,
    decisionReason: null,
    organizationId: null,
    ...parcial,
  };
}

async function estabilizar(fixture: ComponentFixture<RequestAccessPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('RequestAccessPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RequestAccessPage],
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

  async function abrirCom(
    solicitacoes: OrganizerApplication[],
  ): Promise<ComponentFixture<RequestAccessPage>> {
    const fixture = TestBed.createComponent(RequestAccessPage);
    await fixture.whenStable();
    http.expectOne(MINE).flush(solicitacoes);
    await fixture.whenStable();
    return fixture;
  }

  function preencherEEnviar(fixture: ComponentFixture<RequestAccessPage>, nome: string): void {
    const elemento = fixture.nativeElement as HTMLElement;
    const input = elemento.querySelector('input') as HTMLInputElement;
    input.value = nome;
    input.dispatchEvent(new Event('input'));
    (elemento.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
  }

  it('sem solicitação, envia o nome sem espaços e passa a acompanhar o pedido', async () => {
    const fixture = await abrirCom([]);
    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('label')?.textContent).toContain('Nome da organização');

    preencherEEnviar(fixture, '  Liga da Várzea  ');
    await estabilizar(fixture);

    const envio = http.expectOne(SUBMIT);
    expect(envio.request.method).toBe('POST');
    expect(envio.request.body).toEqual({ organizationName: 'Liga da Várzea' });
    envio.flush(solicitacao({}));
    await estabilizar(fixture);

    expect(elemento.textContent).toContain('Solicitação enviada');
    expect(elemento.textContent).toContain('Em análise');
    expect(elemento.querySelector('form')).toBeNull();
  });

  it('recusa nome curto demais sem chamar a API e associa o erro ao campo', async () => {
    const fixture = await abrirCom([]);

    preencherEEnviar(fixture, 'AB');
    await estabilizar(fixture);

    http.expectNone(SUBMIT);
    const elemento = fixture.nativeElement as HTMLElement;
    const input = elemento.querySelector('input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const erro = elemento.querySelector(`#${input.getAttribute('aria-describedby')}`);
    expect(erro?.textContent).toContain('entre 3 e 120 caracteres');
  });

  it('com pedido em análise, mostra o acompanhamento e não oferece novo envio', async () => {
    const fixture = await abrirCom([solicitacao({})]);
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Liga da Várzea');
    expect(elemento.textContent).toContain('Em análise');
    expect(elemento.querySelector('form')).toBeNull();
  });

  it('pedido não aprovado mostra o motivo, o histórico e permite enviar de novo', async () => {
    const fixture = await abrirCom([
      solicitacao({
        status: 'Rejected',
        decidedAt: '2026-09-17T10:00:00+00:00',
        decisionReason: 'Informe o nome oficial da liga.',
      }),
    ]);
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('não foi aprovada');
    expect(elemento.textContent).toContain('Informe o nome oficial da liga.');
    expect(elemento.textContent).toContain('Não aprovada');
    expect(elemento.querySelector('form')).not.toBeNull();
    expect(elemento.textContent).toContain('Enviar nova solicitação');
  });

  it('não mostra o motivo registrado numa aprovação', async () => {
    const fixture = await abrirCom([
      solicitacao({
        status: 'Approved',
        decidedAt: '2026-09-17T10:00:00+00:00',
        decisionReason: 'Anotação interna da análise.',
        organizationId: 'o1',
      }),
    ]);
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('foi aprovada');
    expect(elemento.textContent).not.toContain('Anotação interna da análise.');
  });

  it('falha ao enviar mantém o formulário e mostra a mensagem', async () => {
    const fixture = await abrirCom([]);

    preencherEEnviar(fixture, 'Liga da Várzea');
    await estabilizar(fixture);
    http
      .expectOne(SUBMIT)
      .flush(
        { status: 403, title: 'Modo demonstração' },
        new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }),
      );
    await estabilizar(fixture);

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('[role="alert"]')?.textContent).toContain(
      'Você não tem permissão',
    );
    expect(elemento.querySelector('form')).not.toBeNull();
  });

  it('erro ao carregar é recuperável', async () => {
    const fixture = TestBed.createComponent(RequestAccessPage);
    await fixture.whenStable();
    http
      .expectOne(MINE)
      .flush(
        { status: 500, traceId: '00-rastreio-02' },
        new HttpErrorResponse({ status: 500, statusText: 'Server Error' }),
      );
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('00-rastreio-02');

    (elemento.querySelector('app-button button') as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectOne(MINE).flush([]);
    await fixture.whenStable();

    expect(elemento.querySelector('form')).not.toBeNull();
  });
});
