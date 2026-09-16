import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { SystemInfoPage } from './system-info';
import { SystemInfo } from './system-info.service';

const RESPOSTA: SystemInfo = {
  version: '1.2.3',
  environment: 'Development',
  serverTimeUtc: '2026-09-16T12:30:00+00:00',
  startupCount: 7,
  lastStartedAt: '2026-09-16T12:29:00+00:00',
};

describe('SystemInfoPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SystemInfoPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('mostra o estado de carregando antes da resposta', async () => {
    const fixture = TestBed.createComponent(SystemInfoPage);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Consultando a API');

    http.expectOne('/api/v1/system/info').flush(RESPOSTA);
  });

  it('exibe os dados recebidos da API', async () => {
    const fixture = TestBed.createComponent(SystemInfoPage);
    await fixture.whenStable();

    http.expectOne('/api/v1/system/info').flush(RESPOSTA);
    await fixture.whenStable();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('1.2.3');
    expect(texto).toContain('Development');
    expect(texto).toContain('7');
  });

  it('mostra erro recuperável com traceId e permite tentar de novo', async () => {
    const fixture = TestBed.createComponent(SystemInfoPage);
    await fixture.whenStable();

    http
      .expectOne('/api/v1/system/info')
      .flush(
        { status: 500, traceId: '00-rastreio-01' },
        new HttpErrorResponse({ status: 500, statusText: 'Server Error' }),
      );
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('Algo deu errado do nosso lado');
    expect(elemento.textContent).toContain('00-rastreio-01');

    // O botão de tentar de novo dispara uma requisição nova.
    const botao = elemento.querySelector('app-button button') as HTMLButtonElement;
    botao.click();
    await fixture.whenStable();

    http.expectOne('/api/v1/system/info').flush(RESPOSTA);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('1.2.3');
  });
});
