import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CompetitionSummary } from './competition.service';
import { OrganizationCompetitionsPage } from './organization-competitions';
import { MyOrganization } from './organization.service';

const ORGANIZATION = '/api/v1/organizations/o1';
const COMPETITIONS = '/api/v1/organizations/o1/competitions';

function organizacao(role: MyOrganization['role']): MyOrganization {
  return { id: 'o1', name: 'Liga da Várzea', role, joinedAt: '2026-09-16T12:00:00Z' };
}

const RASCUNHO: CompetitionSummary = {
  id: 'c1',
  name: 'Copa da Várzea',
  season: '2026',
  modality: 'Futsal',
  status: 'Draft',
  updatedAt: '2026-09-16T12:00:00Z',
};

describe('OrganizationCompetitionsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrganizationCompetitionsPage],
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
    campeonatos: CompetitionSummary[],
  ): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(OrganizationCompetitionsPage);
    fixture.componentRef.setInput('organizacao', 'o1');
    await fixture.whenStable();
    http.expectOne(ORGANIZATION).flush(organizacao(papel));
    http.expectOne(COMPETITIONS).flush(campeonatos);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('lista os campeonatos com situação e modalidade em texto', async () => {
    const elemento = await abrir('Owner', [RASCUNHO]);

    expect(elemento.textContent).toContain('Campeonatos de Liga da Várzea');
    const link = elemento.querySelector('a[href="/organizar/c/c1"]');
    expect(link?.textContent).toContain('Copa da Várzea');
    expect(elemento.textContent).toContain('Rascunho');
    expect(elemento.textContent).toContain('Futsal · temporada 2026');
  });

  it('só o proprietário recebe o caminho para criar', async () => {
    const dono = await abrir('Owner', []);
    expect(dono.querySelector('a[href="/organizar/o/o1/campeonatos/novo"]')).not.toBeNull();
    expect(dono.textContent).toContain('Crie o primeiro em rascunho');
  });

  it('auxiliar vê a lista sem o caminho para criar', async () => {
    const auxiliar = await abrir('Assistant', []);
    expect(auxiliar.querySelector('a[href="/organizar/o/o1/campeonatos/novo"]')).toBeNull();
    expect(auxiliar.textContent).toContain('Quem é proprietário da organização cria');
  });

  it('quem não é da organização recebe o aviso sem permissão', async () => {
    const fixture = TestBed.createComponent(OrganizationCompetitionsPage);
    fixture.componentRef.setInput('organizacao', 'o1');
    await fixture.whenStable();
    http
      .expectOne(ORGANIZATION)
      .flush({ title: 'Proibido', status: 403 }, { status: 403, statusText: 'Forbidden' });
    // A lista é cancelada junto: sem permissão na organização, não há o que mostrar.
    expect(http.expectOne(COMPETITIONS).cancelled).toBe(true);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Só quem faz parte da organização vê os campeonatos dela.',
    );
  });
});
