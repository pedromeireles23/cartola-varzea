import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { API_BASE_URL } from '../config/api-base-url';
import { selfServiceGuard } from './auth.guard';
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
