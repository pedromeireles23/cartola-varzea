import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { MyOrganizationsPage } from './my-organizations';
import { MyOrganization } from './organization.service';

const MINE = '/api/v1/organizations/mine';

describe('MyOrganizationsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyOrganizationsPage],
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

  async function abrirCom(organizacoes: MyOrganization[]): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(MyOrganizationsPage);
    await fixture.whenStable();
    http.expectOne(MINE).flush(organizacoes);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('os dois papéis chegam aos campeonatos; só o proprietário, à equipe', async () => {
    const elemento = await abrirCom([
      { id: 'o1', name: 'Liga Própria', role: 'Owner', joinedAt: '2026-09-16T12:00:00Z' },
      { id: 'o2', name: 'Liga Ajudada', role: 'Assistant', joinedAt: '2026-09-16T12:00:00Z' },
    ]);

    const cartoes = [...elemento.querySelectorAll('app-card')];
    const propria = cartoes.find((cartao) => cartao.textContent?.includes('Liga Própria'))!;
    const ajudada = cartoes.find((cartao) => cartao.textContent?.includes('Liga Ajudada'))!;

    const destinos = (cartao: Element) =>
      [...cartao.querySelectorAll('a')].map((link) => link.getAttribute('href'));

    expect(propria.textContent).toContain('Proprietário');
    expect(destinos(propria)).toEqual(['/organizar/o/o1/campeonatos', '/organizar/o/o1/equipe']);
    expect(ajudada.textContent).toContain('Auxiliar');
    expect(destinos(ajudada)).toEqual(['/organizar/o/o2/campeonatos']);
  });

  it('sem organização, explica como pedir acesso ou aceitar convite', async () => {
    const elemento = await abrirCom([]);

    expect(elemento.textContent).toContain('Nenhuma organização ainda');
    expect(elemento.textContent).toContain('convite para auxiliar');
    expect(elemento.querySelector('a[href="/organizar/solicitar"]')).not.toBeNull();
  });
});
