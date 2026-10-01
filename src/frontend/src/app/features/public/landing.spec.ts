import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meta, Title } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';

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

const proximaVolta = () => new Promise((resolve) => setTimeout(resolve, 0));

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

  /**
   * Abre a landing respondendo a busca pública e a pergunta da demonstração. O cartaz
   * do destaque pede calendário e ranking do primeiro campeonato; aqui eles chegam
   * vazios, porque o conteúdo do cartaz é testado na spec dele.
   */
  async function abrir(
    campeonatos: PublicCompetitionSummary[],
    demo = false,
  ): Promise<ComponentFixture<LandingPage>> {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(campeonatos);
    http.expectOne('/api/v1/auth/demo').flush({ available: demo });
    await proximaVolta();
    await fixture.whenStable();

    if (campeonatos.length > 0) {
      const slug = campeonatos[0].slug;
      http
        .expectOne(`${URL}/${slug}/fixtures`)
        .flush({ slug, name: campeonatos[0].name, timeZoneId: 'America/Sao_Paulo', rounds: [] });
      http.expectOne(`${URL}/${slug}/ranking`).flush({
        competitionName: campeonatos[0].name,
        rounds: 0,
        lastRoundName: null,
        provisional: false,
        entries: [],
      });
      await fixture.whenStable();
    }
    return fixture;
  }

  function texto(fixture: ComponentFixture<LandingPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('responde o que é, onde está acontecendo e como funciona', async () => {
    const fixture = await abrir([campeonato()]);

    expect(texto(fixture)).toContain('Monte, dispute, acompanhe.');
    expect(texto(fixture)).toContain('O fantasy do campeonato que você já joga ou assiste');
    expect(texto(fixture)).toContain('Campeonatos publicados');
    expect(texto(fixture)).toContain('Como funciona');
    expect(texto(fixture)).toContain('Monte seu elenco');
    expect(texto(fixture)).toContain('O jogo acontece');
    expect(texto(fixture)).toContain('Os pontos saem');

    const comecar = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      '.capa a.acao',
    );
    expect(comecar?.getAttribute('href')).toBe('/campeonatos');
  });

  it('diz antes do título que não há aposta em dinheiro, e repete abaixo das ações', async () => {
    const fixture = await abrir([]);
    const elemento = fixture.nativeElement as HTMLElement;

    const selo = elemento.querySelector('.capa__selo')?.textContent ?? '';
    expect(selo).toContain('Sem apostas em dinheiro');
    // O selo vem antes do título na ordem de leitura.
    const titulo = elemento.querySelector('h1')!;
    expect(
      elemento.querySelector('.capa__selo')!.compareDocumentPosition(titulo) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    expect(texto(fixture)).toContain('Créditos virtuais, sem pagamento, aposta ou prêmio');
    expect(texto(fixture)).toContain('demonstração de portfólio com dados fictícios');
  });

  it('o estádio é ilustração e fica fora da árvore de acessibilidade', async () => {
    const fixture = await abrir([]);

    const estadio = (fixture.nativeElement as HTMLElement).querySelector('.estadio');
    expect(estadio?.getAttribute('aria-hidden')).toBe('true');
    expect(estadio?.querySelectorAll('.estadio__ficha')).toHaveLength(7);
  });

  it('põe o campeonato mais recente em destaque e os seguintes na vitrine', async () => {
    const fixture = await abrir([
      campeonato(),
      campeonato({ slug: 'copa-do-bairro', name: 'Copa do Bairro', modality: 'Futsal' }),
    ]);
    const elemento = fixture.nativeElement as HTMLElement;

    const destaque = elemento.querySelector<HTMLAnchorElement>('.cartaz__titulo a');
    expect(destaque?.textContent?.trim()).toBe('Copa da Vila');
    expect(destaque?.getAttribute('href')).toBe('/c/copa-da-vila');
    expect(texto(fixture)).toContain('Fut7 · Temporada 2026 · Liga da Vila');

    // O destaque não se repete na lista: o nome dele é um link só.
    const outros = [...elemento.querySelectorAll('.cartao-link__titulo a')];
    expect(outros.map((link) => link.textContent?.trim())).toEqual(['Copa do Bairro']);
    expect(outros[0].getAttribute('href')).toBe('/c/copa-do-bairro');
  });

  it('mostra no máximo três além do destaque, porque o resto é a lista', async () => {
    const fixture = await abrir([
      campeonato({ slug: 'um', name: 'Um' }),
      campeonato({ slug: 'dois', name: 'Dois' }),
      campeonato({ slug: 'tres', name: 'Três' }),
      campeonato({ slug: 'quatro', name: 'Quatro' }),
      campeonato({ slug: 'cinco', name: 'Cinco' }),
    ]);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cartao-link')).toHaveLength(3);
    expect(texto(fixture)).not.toContain('Cinco');
  });

  it('com um só campeonato, não monta uma lista vazia abaixo do destaque', async () => {
    const fixture = await abrir([campeonato()]);

    expect((fixture.nativeElement as HTMLElement).querySelector('.cartoes-link')).toBeNull();
  });

  it('sem campeonato publicado, explica em vez de mostrar lista vazia', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Nenhum campeonato publicado ainda');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-landing-featured')).toBeNull();
  });

  it('a falha dos destaques não derruba a página: o resto continua legível', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/v1/auth/demo').flush({ available: false });
    await proximaVolta();
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não deu para carregar os campeonatos agora');
    expect(texto(fixture)).toContain('Monte, dispute, acompanhe.');
  });

  it('fora da demonstração, a ação principal é ver os campeonatos', async () => {
    const fixture = await abrir([], false);

    expect(texto(fixture)).not.toContain('Entrar como visitante');
  });

  it('na demonstração, o visitante entra a um clique e vai para o início', async () => {
    const fixture = await abrir([], true);
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.includes('Entrar como visitante'),
    )!;
    botao.click();
    await proximaVolta();

    http
      .expectOne({ method: 'POST', url: '/api/v1/auth/demo' })
      .flush(null, { status: 204, statusText: 'No Content' });
    await proximaVolta();
    http.expectOne('/api/v1/auth/antiforgery').flush(null);
    await proximaVolta();
    http.expectOne('/api/v1/auth/me').flush({
      id: 'u1',
      email: 'visitante@demo.test',
      displayName: 'Visitante',
      emailConfirmed: true,
      roles: ['DemoViewer'],
    });
    await proximaVolta();

    expect(navegar).toHaveBeenCalledWith('/inicio');
  });

  it('se a entrada de visitante falhar, diz isso sem sair da página', async () => {
    const fixture = await abrir([], true);

    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.includes('Entrar como visitante'),
    )!;
    botao.click();
    await proximaVolta();

    http
      .expectOne({ method: 'POST', url: '/api/v1/auth/demo' })
      .flush(null, { status: 404, statusText: 'Not Found' });
    await proximaVolta();
    await fixture.whenStable();

    expect(texto(fixture)).toContain('A entrada de visitante não está disponível agora');
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
