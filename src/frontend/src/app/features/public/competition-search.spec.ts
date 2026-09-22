import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CompetitionSearchPage } from './competition-search';
import { PublicCompetitionSummary } from './public-competition.service';

const BUSCA = '/api/v1/public/competitions';

function campeonato(parcial: Partial<PublicCompetitionSummary> = {}): PublicCompetitionSummary {
  return {
    slug: 'copa-da-varzea-2026',
    name: 'Copa da Várzea',
    season: '2026',
    modality: 'Fut7',
    organizationName: 'Liga da Várzea',
    publishedAt: '2026-09-17T12:00:00Z',
    ...parcial,
  };
}

describe('CompetitionSearchPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionSearchPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'campeonatos', component: CompetitionSearchPage }]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(
    resposta: PublicCompetitionSummary[],
  ): Promise<ComponentFixture<CompetitionSearchPage>> {
    const fixture = TestBed.createComponent(CompetitionSearchPage);
    await fixture.whenStable();
    http.expectOne(BUSCA).flush(resposta);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionSearchPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('lista os campeonatos publicados com link pelo slug', async () => {
    const fixture = await abrir([campeonato()]);

    expect(texto(fixture)).toContain('Copa da Várzea');
    expect(texto(fixture)).toContain('Liga da Várzea');
    expect(texto(fixture)).toContain('Fut7');
    const link = (fixture.nativeElement as HTMLElement).querySelector('.cartao-link__titulo a');
    expect(link?.getAttribute('href')).toBe('/c/copa-da-varzea-2026');
  });

  it('busca envia o termo e o leva para a URL', async () => {
    const fixture = await abrir([campeonato()]);
    const router = TestBed.inject(Router);

    (fixture.nativeElement as HTMLElement).querySelector('input')!.value = '  bairro  ';
    (fixture.nativeElement as HTMLElement)
      .querySelector('input')!
      .dispatchEvent(new Event('input'));
    await fixture.whenStable();
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const requisicao = http.expectOne((pedido) => pedido.url === BUSCA);
    expect(requisicao.request.params.get('busca')).toBe('bairro');
    requisicao.flush([]);
    await fixture.whenStable();

    expect(router.url).toContain('busca=bairro');
    expect(texto(fixture)).toContain('Nenhum campeonato publicado corresponde a essa busca.');
  });

  it('sem nada publicado, o vazio não parece falha de busca', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Nenhum campeonato publicado ainda.');
  });

  it('erro de rede oferece tentar de novo', async () => {
    const fixture = TestBed.createComponent(CompetitionSearchPage);
    await fixture.whenStable();
    http.expectOne(BUSCA).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Algo deu errado do nosso lado.');

    [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')]
      .find((botao) => botao.textContent?.trim() === 'Tentar de novo')
      ?.click();
    await fixture.whenStable();
    http.expectOne(BUSCA).flush([campeonato()]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Copa da Várzea');
  });
});
