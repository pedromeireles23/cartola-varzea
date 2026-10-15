import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanMatchFn,
  Route,
  Router,
  RouterStateSnapshot,
  UrlSegment,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { API_BASE_URL } from '../config/api-base-url';
import { platformAdminGuard, selfServiceGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('selfServiceGuard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function publicacao(selfService: boolean): Promise<void> {
    const consulta = TestBed.inject(AuthService).loadDemoAccess();
    http.expectOne('/api/v1/auth/demo').flush({ available: true, selfService });
    await consulta;
  }

  function ativar(): unknown {
    return TestBed.runInInjectionContext(() =>
      selfServiceGuard({} as ActivatedRouteSnapshot, { url: '/cadastro' } as RouterStateSnapshot),
    );
  }

  it('deixa abrir o cadastro quando a publicação tem e-mail', async () => {
    await publicacao(true);

    expect(ativar()).toBe(true);
  });

  it('manda o link direto do cadastro para a entrada quando não há e-mail', async () => {
    await publicacao(false);

    const destino = ativar() as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(destino)).toBe('/entrar');
  });
});

describe('platformAdminGuard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function casar(conta: { roles: string[] } | null): Promise<unknown> {
    const resultado = TestBed.runInInjectionContext(() =>
      platformAdminGuard({} as Route, [] as UrlSegment[], {} as Parameters<CanMatchFn>[2]),
    ) as Promise<unknown>;
    http
      .expectOne('/api/v1/auth/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    const me = http.expectOne('/api/v1/auth/me');
    if (conta) {
      me.flush({ id: '1', email: 'a@b.c', displayName: 'A', emailConfirmed: true, ...conta });
    } else {
      me.flush(null, { status: 204, statusText: 'No Content' });
    }
    return resultado;
  }

  it('casa a rota para o Platform admin', async () => {
    expect(await casar({ roles: ['PlatformAdmin'] })).toBe(true);
  });

  it('não casa para outra conta, sem mandar para a entrada', async () => {
    expect(await casar({ roles: ['Organizer'] })).toBe(false);
  });

  it('não casa sem sessão', async () => {
    expect(await casar(null)).toBe(false);
  });
});
