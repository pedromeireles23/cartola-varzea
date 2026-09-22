import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meta, Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { LandingPage } from './landing';
import { PublicCompetitionSummary } from './public-competition.service';

const URL = '/api/v1/public/competitions';

function campeonato(changes: Partial<PublicCompetitionSummary> = {}): PublicCompetitionSummary {
  return {
    slug: 'copa-da-vila',
    name: 'Copa da Vila',
    season: '2026',
    modality: 'Fut7',
    organizationName: 'Liga da Vila',
    publishedAt: '2026-09-01T12:00:00Z',
    ...changes,
  };
}

describe('LandingPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LandingPage],
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
    campeonatos: PublicCompetitionSummary[],
  ): Promise<ComponentFixture<LandingPage>> {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(campeonatos);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<LandingPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('responde o que é, como funciona e onde começar', async () => {
    const fixture = await abrir([campeonato()]);

    expect(texto(fixture)).toContain('Monte, dispute, acompanhe.');
    expect(texto(fixture)).toContain('O fantasy do campeonato que você já joga ou assiste');
    expect(texto(fixture)).toContain('Como funciona');
    expect(texto(fixture)).toContain('Monte seu elenco');
    expect(texto(fixture)).toContain('O jogo acontece');
    expect(texto(fixture)).toContain('Os pontos saem');

    const comecar = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.acao',
    );
    expect(comecar?.getAttribute('href')).toBe('/campeonatos');
  });

  it('diz logo que não há pagamento, aposta nem prêmio', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Créditos virtuais, sem pagamento, aposta ou prêmio');
    expect(texto(fixture)).toContain('demonstração de portfólio com dados fictícios');
  });

  it('destaca os campeonatos publicados e leva a cada um', async () => {
    const fixture = await abrir([
      campeonato(),
      campeonato({ slug: 'copa-do-bairro', name: 'Copa do Bairro', modality: 'Futsal' }),
    ]);

    expect(texto(fixture)).toContain('Copa da Vila');
    expect(texto(fixture)).toContain('Fut7 · Temporada 2026 · Liga da Vila');
    expect(texto(fixture)).toContain('Copa do Bairro');

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      '.cartao-link__titulo a',
    );
    expect(link?.getAttribute('href')).toBe('/c/copa-da-vila');
  });

  it('mostra no máximo três destaques, porque o resto é a lista', async () => {
    const fixture = await abrir([
      campeonato({ slug: 'um', name: 'Um' }),
      campeonato({ slug: 'dois', name: 'Dois' }),
      campeonato({ slug: 'tres', name: 'Três' }),
      campeonato({ slug: 'quatro', name: 'Quatro' }),
    ]);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cartao-link')).toHaveLength(3);
    expect(texto(fixture)).not.toContain('Quatro');
  });

  it('sem campeonato publicado, explica em vez de mostrar lista vazia', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Nenhum campeonato publicado ainda');
    expect((fixture.nativeElement as HTMLElement).querySelector('.cartao-link')).toBeNull();
  });

  it('a falha dos destaques não derruba a página: o resto continua legível', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não deu para carregar os campeonatos agora');
    expect(texto(fixture)).toContain('Monte, dispute, acompanhe.');
  });

  it('publica título e metadados de compartilhamento', async () => {
    await abrir([]);

    expect(TestBed.inject(Title).getTitle()).toBe(
      'Fantasy para campeonatos de várzea · Cartola Várzea',
    );
    const meta = TestBed.inject(Meta);
    expect(meta.getTag('property="og:title"')?.content).toContain('Fantasy para campeonatos');
    expect(meta.getTag('name="description"')?.content).toContain('Monte seu elenco');
    expect(meta.getTag('property="og:type"')?.content).toBe('website');
  });
});
