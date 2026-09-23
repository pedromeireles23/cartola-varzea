import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import {
  MARKET_URL,
  MERCADO_FECHADO,
  OVERVIEW_URL,
  SLUG,
  entrada,
  item,
  mercado,
  visao,
} from './fantasy-fixtures';
import { FantasyMarketPage } from './fantasy-market';
import { FantasyNotice } from './fantasy-notice';
import { FantasyMarket, FantasyOverview } from './fantasy.service';

describe('FantasyMarketPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyMarketPage],
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
    dadosVisao: FantasyOverview,
    dadosMercado: FantasyMarket,
    posicao?: string,
    origem?: string,
  ): Promise<ComponentFixture<FantasyMarketPage>> {
    const fixture = TestBed.createComponent(FantasyMarketPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    if (posicao) {
      fixture.componentRef.setInput('posicao', posicao);
    }
    if (origem) {
      fixture.componentRef.setInput('origem', origem);
    }
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(dadosVisao);
    http.expectOne(MARKET_URL).flush(dadosMercado);
    await fixture.whenStable();
    return fixture;
  }

  function elemento(fixture: ComponentFixture<FantasyMarketPage>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function texto(fixture: ComponentFixture<FantasyMarketPage>): string {
    return elemento(fixture).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function times(fixture: ComponentFixture<FantasyMarketPage>): string[] {
    return [...elemento(fixture).querySelectorAll('h2.grupo__nome')].map((titulo) =>
      titulo.textContent!.trim(),
    );
  }

  function botao(fixture: ComponentFixture<FantasyMarketPage>, rotulo: string): HTMLButtonElement {
    const achado = [...elemento(fixture).querySelectorAll('button')].find(
      (button) => button.textContent?.replace(/\s+/g, ' ').trim() === rotulo,
    );
    expect(achado, `Botão "${rotulo}" não encontrado`).toBeDefined();
    return achado!;
  }

  /** A posição é um grupo de botões: um toque, não um menu. */
  function posicao(fixture: ComponentFixture<FantasyMarketPage>, rotulo: string): void {
    const opcao = [...elemento(fixture).querySelectorAll('.posicoes button')].find(
      (button) => button.textContent?.trim() === rotulo,
    ) as HTMLButtonElement;
    expect(opcao, `Posição "${rotulo}" não encontrada`).toBeDefined();
    opcao.click();
  }

  function selecionar(
    fixture: ComponentFixture<FantasyMarketPage>,
    rotulo: string,
    valor: string,
  ): void {
    const campo = [...elemento(fixture).querySelectorAll('select')].find((select) =>
      select.labels?.[0]?.textContent?.includes(rotulo),
    )!;
    campo.value = valor;
    campo.dispatchEvent(new Event('change'));
  }

  it('agrupa por time, mostra o limite do time e o motivo do bloqueio no próprio item', async () => {
    const fixture = await abrir(
      visao({
        entry: entrada({
          slots: [
            {
              kind: 'Athlete',
              assetId: 'a2',
              name: 'Caio',
              position: 'Forward',
              realTeamId: 't2',
              realTeamName: 'Estrela',
              role: 'Starter',
              currentPrice: 9,
              purchasePrice: 9,
              isAvailable: true,
              isCaptain: false,
            },
          ],
        }),
      }),
      mercado([
        item(),
        item({
          id: 'a2',
          name: 'Caio',
          position: 'Forward',
          realTeamId: 't2',
          realTeamName: 'Estrela',
          isOwned: true,
          blockCode: 'already_owned',
          blockReason: 'Esse ativo já está no seu elenco.',
        }),
        item({
          id: 'a3',
          name: 'Duda',
          position: 'Goalkeeper',
          realTeamId: 't2',
          realTeamName: 'Estrela',
          price: 30,
          blockCode: 'insufficient_balance',
          blockReason: 'Faltam C$ 2,00.',
        }),
        item({
          id: 'c1',
          kind: 'Coach',
          name: 'Técnico do Aurora',
          position: null,
          realTeamId: 't3',
          realTeamName: 'Aurora',
        }),
      ]),
    );

    expect(times(fixture)).toEqual(['Aurora', 'Estrela', 'União da Vila']);
    expect(texto(fixture)).toContain('No seu elenco: 1 de 5 atletas');
    expect(texto(fixture)).toContain('Faltam C$ 2,00.');
    expect(texto(fixture)).not.toContain('Esse ativo já está no seu elenco.');
    const tecnico = [...elemento(fixture).querySelectorAll('.item')].find((linha) =>
      linha.textContent?.includes('Técnico do Aurora'),
    );
    expect(tecnico?.querySelector('.item__detalhe')?.textContent?.trim()).toBe('Técnico');
    expect(tecnico?.querySelector('.item__preco')?.textContent?.trim()).toBe('C$ 8,00');
    expect(botao(fixture, 'Comprar Duda').disabled).toBe(true);
    expect(botao(fixture, 'Comprar Bia').disabled).toBe(false);
    expect(botao(fixture, 'Vender Caio').disabled).toBe(false);
  });

  it('compra, relê o mercado e anuncia o novo saldo', async () => {
    const fixture = await abrir(visao(), mercado([item()]));

    botao(fixture, 'Comprar Bia').click();
    await fixture.whenStable();
    const compra = http.expectOne(`/api/v1/fantasy/${SLUG}/squad/atleta/a1`);
    expect(compra.request.method).toBe('POST');
    compra.flush(visao({ entry: entrada({ balance: 92 }) }));
    await fixture.whenStable();
    http
      .expectOne(MARKET_URL)
      .flush(mercado([item({ isOwned: true, blockCode: 'already_owned' })]));
    await fixture.whenStable();

    expect(elemento(fixture).querySelector('[role="status"]')?.textContent).toContain(
      'Bia entrou no seu elenco. Saldo: C$ 92,00.',
    );
    expect(botao(fixture, 'Vender Bia')).toBeDefined();
  });

  it('mostra a recusa do servidor e relê tudo quando o mercado fechou no meio', async () => {
    const fixture = await abrir(visao(), mercado([item()]));

    botao(fixture, 'Comprar Bia').click();
    await fixture.whenStable();
    http.expectOne(`/api/v1/fantasy/${SLUG}/squad/atleta/a1`).flush(
      {
        title: 'Mercado fechado',
        status: 409,
        detail: 'O mercado está fechado.',
        code: 'fantasy_market_closed',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(visao({ market: MERCADO_FECHADO }));
    http.expectOne(MARKET_URL).flush(
      mercado([item({ blockCode: 'market_closed', blockReason: 'O mercado está fechado.' })], {
        market: MERCADO_FECHADO,
      }),
    );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Mercado fechado');
    expect(elemento(fixture).querySelectorAll('.itens button')).toHaveLength(0);
  });

  it('sem participação, deixa olhar e aponta para a entrada, sem botões de compra', async () => {
    const fixture = await abrir(
      visao({ entry: null }),
      mercado(
        [item({ blockCode: 'not_joined', blockReason: 'Entre no campeonato para comprar.' })],
        {
          balance: null,
        },
      ),
    );

    expect(texto(fixture)).toContain('Você ainda não entrou neste campeonato.');
    expect(texto(fixture)).not.toContain('Entre no campeonato para comprar.');
    expect(elemento(fixture).querySelectorAll('.itens button')).toHaveLength(0);
  });

  it('entra filtrado pela posição do slot e mantém o agrupamento', async () => {
    const fixture = await abrir(
      visao(),
      mercado([
        item({ id: 'g1', name: 'Nena', position: 'Goalkeeper' }),
        item({
          id: 'g2',
          name: 'Paredão',
          position: 'Goalkeeper',
          realTeamId: 't2',
          realTeamName: 'Estrela',
        }),
        item({ id: 'f1', name: 'Caio', position: 'Forward' }),
      ]),
      'goleiro',
    );

    expect(times(fixture)).toEqual(['Estrela', 'União da Vila']);
    expect(texto(fixture)).toContain('Nena');
    expect(texto(fixture)).not.toContain('Caio');

    posicao(fixture, 'Todas');
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Caio');
  });

  it('aberto por uma vaga do campo, volta para a escalação dizendo onde o atleta entrou', async () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = await abrir(
      visao(),
      mercado([item({ id: 'g1', name: 'Nena', position: 'Goalkeeper' })]),
      'goleiro',
      'escalacao',
    );

    expect(texto(fixture)).toContain('Escolhendo um goleiro para a escalação.');
    botao(fixture, 'Comprar Nena').click();
    await fixture.whenStable();
    http.expectOne(`/api/v1/fantasy/${SLUG}/squad/atleta/g1`).flush(
      visao({
        entry: entrada({
          balance: 93,
          slots: [
            {
              kind: 'Athlete',
              assetId: 'g1',
              name: 'Nena',
              position: 'Goalkeeper',
              realTeamId: 't1',
              realTeamName: 'União da Vila',
              role: 'Bench',
              currentPrice: 7,
              purchasePrice: 7,
              isAvailable: true,
              isCaptain: false,
            },
          ],
        }),
      }),
    );
    await fixture.whenStable();

    // Sem reler o mercado: a tela seguinte é o campo.
    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'escalacao']);
    expect(TestBed.inject(FantasyNotice).retirar()).toBe('Nena entrou no banco. Saldo: C$ 93,00.');
  });

  it('pagina por times e volta ao início quando o filtro muda', async () => {
    const nomes = ['Aurora', 'Brisa', 'Cometa', 'Dragão', 'Estrela', 'Farol', 'Gávea', 'Horizonte'];
    const fixture = await abrir(
      visao(),
      mercado(
        nomes.map((nome, indice) =>
          item({
            id: `a${indice}`,
            name: `Atleta ${indice}`,
            realTeamId: `t${indice}`,
            realTeamName: nome,
            price: indice + 1,
          }),
        ),
      ),
    );

    expect(times(fixture)).toHaveLength(6);
    botao(fixture, 'Mostrar mais times (2)').click();
    await fixture.whenStable();
    expect(times(fixture)).toHaveLength(8);

    selecionar(fixture, 'Situação', 'compraveis');
    await fixture.whenStable();
    expect(times(fixture)).toHaveLength(6);
  });

  it('ordenar por preço desfaz o agrupamento e mostra o time em cada linha', async () => {
    const fixture = await abrir(
      visao(),
      mercado([
        item({ id: 'a1', name: 'Caro', price: 12 }),
        item({
          id: 'a2',
          name: 'Barato',
          price: 5,
          realTeamId: 't2',
          realTeamName: 'Estrela',
        }),
        item({ id: 'a3', name: 'Médio', price: 8 }),
      ]),
    );

    selecionar(fixture, 'Ordenar', 'menor');
    await fixture.whenStable();

    expect(times(fixture)).toEqual([]);
    const nomes = [...elemento(fixture).querySelectorAll('.item__nome')].map((nome) =>
      nome.textContent!.trim(),
    );
    expect(nomes).toEqual(['Barato', 'Médio', 'Caro']);
    expect(texto(fixture)).toContain('Meio-campista · Estrela');
    expect(texto(fixture)).toContain('3 opções');
    expect(texto(fixture)).not.toContain('3 opções em');
  });
});
