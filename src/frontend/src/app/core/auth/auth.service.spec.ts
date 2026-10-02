import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { apiErrorInterceptor } from '../api/api-error.interceptor';
import { API_BASE_URL } from '../config/api-base-url';
import { Account, AuthService } from './auth.service';

/** Drena a fila de microtasks: a próxima requisição só sai depois do await anterior. */
const proximaRequisicao = () => new Promise((resolve) => setTimeout(resolve, 0));

const CONTA: Account = {
  id: '018f0000-0000-7000-8000-000000000000',
  email: 'pessoa@exemplo.local',
  displayName: 'Pessoa',
  emailConfirmed: true,
  roles: [],
};

describe('AuthService', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('pede o token antiforgery e a sessão juntos, sem um esperar o outro', async () => {
    const carregando = auth.load();

    // As duas saem na mesma volta: a primeira tela não paga duas idas e voltas.
    const antiforgery = http.expectOne('/api/v1/auth/antiforgery');
    http.expectOne('/api/v1/auth/me').flush(CONTA);
    antiforgery.flush(null, { status: 204, statusText: 'No Content' });
    await carregando;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.current()?.email).toBe(CONTA.email);
    expect(auth.ready()).toBe(true);
  });

  it('sabe se a demonstração liga a entrada de visitante', async () => {
    expect(auth.demoAccess()).toBe(false);

    const consulta = auth.loadDemoAccess();
    http.expectOne('/api/v1/auth/demo').flush({ available: true });
    await consulta;

    expect(auth.demoAccess()).toBe(true);
  });

  it('sem resposta da demonstração, não oferece a entrada de visitante', async () => {
    const consulta = auth.loadDemoAccess();
    http.expectOne('/api/v1/auth/demo').flush(null, { status: 500, statusText: 'Server Error' });
    await consulta;

    expect(auth.demoAccess()).toBe(false);
  });

  it('trata 204 de /me como sessão anônima, não como erro', async () => {
    const carregando = auth.load();

    http
      .expectOne('/api/v1/auth/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await proximaRequisicao();

    http.expectOne('/api/v1/auth/me').flush(null, { status: 204, statusText: 'No Content' });
    await carregando;

    expect(auth.current()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.ready()).toBe(true);
  });

  it('renova o token antiforgery depois de entrar', async () => {
    const entrando = auth.login('pessoa@exemplo.local', 'uma-senha-bem-longa-2026');

    http.expectOne('/api/v1/auth/login').flush(null, { status: 204, statusText: 'No Content' });
    await proximaRequisicao();

    // O token é vinculado à identidade: o emitido para o anônimo deixa de valer.
    http
      .expectOne('/api/v1/auth/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await proximaRequisicao();

    http.expectOne('/api/v1/auth/me').flush(CONTA);
    await entrando;

    expect(auth.isAuthenticated()).toBe(true);
  });

  it('renova o token antiforgery depois de sair e limpa a sessão', async () => {
    const saindo = auth.logout();

    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await proximaRequisicao();

    http
      .expectOne('/api/v1/auth/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await saindo;

    expect(auth.current()).toBeNull();
  });
});
