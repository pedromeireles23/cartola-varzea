import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { OrganizerApplicationsPage } from './organizer-applications';
import { PendingOrganizerApplication } from './organizer-review.service';

const PENDING = '/api/v1/platform-admin/organizer-applications/pending';

const PEDIDO: PendingOrganizerApplication = {
  id: 'a1',
  organizationName: 'Liga da Várzea',
  submittedAt: '2026-09-16T12:30:00+00:00',
  applicantUserId: 'u1',
  applicantDisplayName: 'Maria Organizadora',
  applicantEmail: 'maria@exemplo.local',
};

async function estabilizar(fixture: ComponentFixture<OrganizerApplicationsPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('OrganizerApplicationsPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    // O jsdom não implementa o modal nativo; o comportamento de foco e Esc é do
    // navegador e fica coberto pelo E2E.
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
      imports: [OrganizerApplicationsPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrirCom(
    fila: PendingOrganizerApplication[],
  ): Promise<ComponentFixture<OrganizerApplicationsPage>> {
    const fixture = TestBed.createComponent(OrganizerApplicationsPage);
    await fixture.whenStable();
    http.expectOne(PENDING).flush(fila);
    await fixture.whenStable();
    return fixture;
  }

  function botao(fixture: ComponentFixture<OrganizerApplicationsPage>, texto: string) {
    const botoes = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];
    const encontrado = botoes.find((item) => item.textContent?.trim() === texto);
    expect(encontrado, `Botão "${texto}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  function escreverMotivo(fixture: ComponentFixture<OrganizerApplicationsPage>, texto: string) {
    const campo = (fixture.nativeElement as HTMLElement).querySelector(
      'textarea',
    ) as HTMLTextAreaElement;
    campo.value = texto;
    campo.dispatchEvent(new Event('input'));
  }

  it('lista quem pediu e a organização', async () => {
    const fixture = await abrirCom([PEDIDO]);
    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(texto).toContain('Liga da Várzea');
    expect(texto).toContain('Maria Organizadora');
    expect(texto).toContain('maria@exemplo.local');
  });

  it('fila vazia diz que não há nada para analisar', async () => {
    const fixture = await abrirCom([]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Nenhuma solicitação aguardando análise',
    );
  });

  it('recusa exige motivo, avisa que quem pediu vai lê-lo e tira o pedido da fila', async () => {
    const fixture = await abrirCom([PEDIDO]);

    botao(fixture, 'Não aprovar').click();
    await estabilizar(fixture);
    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('Quem pediu vai ler este motivo');

    botao(fixture, 'Confirmar recusa').click();
    await estabilizar(fixture);
    http.expectNone(`/api/v1/platform-admin/organizer-applications/a1/reject`);
    expect(elemento.querySelector('textarea')?.getAttribute('aria-invalid')).toBe('true');

    escreverMotivo(fixture, '  Informe o nome oficial da liga.  ');
    botao(fixture, 'Confirmar recusa').click();
    await estabilizar(fixture);

    const envio = http.expectOne('/api/v1/platform-admin/organizer-applications/a1/reject');
    expect(envio.request.body).toEqual({ reason: 'Informe o nome oficial da liga.' });
    envio.flush({});
    await estabilizar(fixture);

    expect(elemento.textContent).toContain('não foi aprovada');
    expect(elemento.textContent).toContain('Nenhuma solicitação aguardando análise');
  });

  it('aprovação avisa que o motivo fica só na auditoria', async () => {
    const fixture = await abrirCom([PEDIDO]);

    botao(fixture, 'Aprovar').click();
    await estabilizar(fixture);
    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('quem pediu não vê');

    escreverMotivo(fixture, 'Responsável conhecido da liga.');
    botao(fixture, 'Confirmar aprovação').click();
    await estabilizar(fixture);

    http.expectOne('/api/v1/platform-admin/organizer-applications/a1/approve').flush({});
    await estabilizar(fixture);
    expect(elemento.textContent).toContain('Liga da Várzea foi aprovada');
  });

  it('decisão tomada por outra pessoa recarrega a fila em vez de mostrar erro de formulário', async () => {
    const fixture = await abrirCom([PEDIDO]);

    botao(fixture, 'Aprovar').click();
    await estabilizar(fixture);
    escreverMotivo(fixture, 'Responsável conhecido da liga.');
    botao(fixture, 'Confirmar aprovação').click();
    await estabilizar(fixture);

    http
      .expectOne('/api/v1/platform-admin/organizer-applications/a1/approve')
      .flush({}, new HttpErrorResponse({ status: 409, statusText: 'Conflict' }));
    await estabilizar(fixture);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('já tinha sido decidida');
    http.expectOne(PENDING).flush([]);
  });

  it('conta sem papel de administração vê o aviso de acesso restrito', async () => {
    const fixture = TestBed.createComponent(OrganizerApplicationsPage);
    await fixture.whenStable();
    http
      .expectOne(PENDING)
      .flush({ status: 403 }, new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('exclusiva da administração da plataforma');
    expect(elemento.textContent).not.toContain('Tentar de novo');
  });
});
