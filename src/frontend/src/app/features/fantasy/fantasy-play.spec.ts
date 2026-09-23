import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import {
  MERCADO_FECHADO,
  OVERVIEW_URL,
  ROUNDS_URL,
  SLUG,
  entrada,
  rodadaResumo,
  visao,
} from './fantasy-fixtures';
import { FantasyPlayPage } from './fantasy-play';

describe('FantasyPlayPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyPlayPage],
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

  async function abrir(): Promise<ComponentFixture<FantasyPlayPage>> {
    const fixture = TestBed.createComponent(FantasyPlayPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<FantasyPlayPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('explica o que a pessoa recebe e entra com um clique', async () => {
    const fixture = await abrir();
    http.expectOne(OVERVIEW_URL).flush(visao({ entry: null }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Ao entrar você recebe C$ 100,00');
    expect(texto(fixture)).toContain('Até 5 atletas do mesmo time, sendo 3 titulares');
    expect(texto(fixture)).toContain('Rodada 1 · fecha');
    expect(texto(fixture)).toContain('(Horário de Brasília)');

    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.includes('Entrar no campeonato'),
    )!;
    botao.click();
    await fixture.whenStable();

    const pedido = http.expectOne(`/api/v1/fantasy/${SLUG}/entry`);
    expect(pedido.request.method).toBe('POST');
    pedido.flush(visao());
    await fixture.whenStable();
    http.expectOne(ROUNDS_URL).flush([]);
    // A navegação do jogo relê os campeonatos da conta para mostrar o recém-chegado.
    http.expectOne('/api/v1/fantasy').flush([]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Você entrou no campeonato e recebeu C$ 100,00.');
    expect(texto(fixture)).toContain('Falta o técnico.');
    expect(texto(fixture)).not.toContain('Entrar no campeonato');
  });

  it('mostra saldo, patrimônio e a escalação completa com o mercado fechado', async () => {
    const fixture = await abrir();
    http.expectOne(OVERVIEW_URL).flush(
      visao({
        market: MERCADO_FECHADO,
        entry: entrada({
          balance: 3.5,
          patrimony: 101,
          issues: [],
          slots: [
            {
              kind: 'Coach',
              assetId: 'c1',
              name: 'Técnico do União da Vila',
              position: null,
              realTeamId: 't1',
              realTeamName: 'União da Vila',
              role: 'Coach',
              currentPrice: 8,
              purchasePrice: 8,
              isAvailable: true,
              isCaptain: false,
            },
          ],
        }),
      }),
    );
    await fixture.whenStable();
    http.expectOne(ROUNDS_URL).flush([rodadaResumo({ total: 21.5 })]);
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Rodadas apuradas');
    expect(texto(fixture)).toContain('21,50 pts');
    expect(texto(fixture)).toContain('provisório');
    expect(texto(fixture)).toContain('Mercado fechado');
    expect(texto(fixture)).toContain('C$ 3,50');
    expect(texto(fixture)).toContain('C$ 101,00');
    expect(texto(fixture)).toContain('Técnico do União da Vila');
    expect(texto(fixture)).toContain('Escalação completa');
    const mercado = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a.acao[href="/c/${SLUG}/mercado"]`,
    );
    expect(mercado?.textContent).toContain('Abrir o mercado');
    // A montagem começa pelo campo (01 §12): é a ação principal da tela.
    const escalar = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a.acao[href="/c/${SLUG}/escalacao"]`,
    );
    expect(escalar?.textContent).toContain('Escalar meu time');
  });

  it('não confunde campeonato inexistente com erro de rede', async () => {
    const fixture = await abrir();
    http.expectOne(OVERVIEW_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato.');
  });
});
