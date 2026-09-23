import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import {
  LINEUP_URL,
  MARKET_URL,
  MERCADO_FECHADO,
  OVERVIEW_URL,
  SLUG,
  entrada,
  item,
  mercado,
  vaga,
  visao,
} from './fantasy-fixtures';
import { FantasyLineupPage } from './fantasy-lineup';
import { FantasyNotice } from './fantasy-notice';
import { ClosedRoundLineup, FantasyOverview } from './fantasy.service';

/** Goleiro e dois defensores titulares, um defensor no banco e o técnico. */
const ELENCO = [
  vaga({ assetId: 'g1', name: 'Pipoca', position: 'Goalkeeper' }),
  vaga({ assetId: 'd1', name: 'Baiano', position: 'Defender', isCaptain: true }),
  vaga({ assetId: 'd2', name: 'Zeca', position: 'Defender', realTeamName: 'Estrela do Bairro' }),
  vaga({ assetId: 'd3', name: 'Tanque', position: 'Defender', role: 'Bench' }),
  vaga({
    assetId: 'c1',
    kind: 'Coach',
    name: 'Seu Zé',
    position: null,
    role: 'Coach',
    currentPrice: 11,
  }),
];

describe('FantasyLineupPage', () => {
  let http: HttpTestingController;

  beforeAll(() => {
    // O jsdom não fecha o <dialog> com o evento `close`, que é o que a tela escuta.
    const prototipo = HTMLDialogElement.prototype as HTMLDialogElement & {
      showModal?: () => void;
      show?: () => void;
    };
    prototipo.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototipo.show ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototipo.close = function (this: HTMLDialogElement) {
      if (this.hasAttribute('open')) {
        this.removeAttribute('open');
        this.dispatchEvent(new Event('close'));
      }
    };
  });

  beforeEach(() => {
    globalThis.localStorage?.clear();
    TestBed.configureTestingModule({
      imports: [FantasyLineupPage],
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

  async function abrir(dados: FantasyOverview): Promise<ComponentFixture<FantasyLineupPage>> {
    const fixture = TestBed.createComponent(FantasyLineupPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(OVERVIEW_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function elemento(fixture: ComponentFixture<FantasyLineupPage>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function texto(fixture: ComponentFixture<FantasyLineupPage>): string {
    return elemento(fixture).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  /** Nome acessível da vaga: é o texto escondido que o leitor de tela lê. */
  function vagas(fixture: ComponentFixture<FantasyLineupPage>, seletor: string): string[] {
    return [...elemento(fixture).querySelectorAll(`${seletor} > .sr-only`)].map((rotulo) =>
      rotulo.textContent!.trim(),
    );
  }

  function botao(fixture: ComponentFixture<FantasyLineupPage>, rotulo: string): HTMLButtonElement {
    const achado = [...elemento(fixture).querySelectorAll('button')].find((button) =>
      button.textContent?.replace(/\s+/g, ' ').trim().startsWith(rotulo),
    );
    expect(achado, `Botão "${rotulo}" não encontrado`).toBeDefined();
    return achado!;
  }

  function painel(fixture: ComponentFixture<FantasyLineupPage>): HTMLDialogElement {
    return elemento(fixture).querySelector('dialog.painel')!;
  }

  function fechada(parcial: Partial<ClosedRoundLineup>): ClosedRoundLineup {
    return {
      roundName: 'Rodada 1',
      marketClosedAt: '2099-09-20T22:00:00Z',
      marketClosedAtLocal: '2099-09-20T19:00',
      status: 'Frozen',
      captainAthleteId: null,
      slots: [],
      ...parcial,
    };
  }

  it('desenha o campo vazio com cada vaga pronta para escolher quem entra', async () => {
    const fixture = await abrir(visao());

    expect(texto(fixture)).toContain('Titulares 1-2-2-2');
    expect(vagas(fixture, 'button.ficha--vazia')).toEqual([
      'Escolher atacante titular',
      'Escolher atacante titular',
      'Escolher meio-campista titular',
      'Escolher meio-campista titular',
      'Escolher defensor titular',
      'Escolher defensor titular',
      'Escolher goleiro titular',
      'Escolher reserva de goleiro',
      'Escolher reserva de defensor',
      'Escolher reserva de meio-campista',
      'Escolher reserva de atacante',
      'Escolher técnico',
    ]);
    expect(texto(fixture)).toContain('0 de 12');
    expect(texto(fixture)).toContain('O que falta para a escalação valer');
    expect(texto(fixture)).toContain('Se o mercado fechar com algo faltando, você fica fora da');
  });

  it('a vaga vazia abre a escolha da posição e compra sem sair do campo', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO.slice(1) }) }));

    botao(fixture, 'Escolher goleiro titular').click();
    await fixture.whenStable();
    expect(painel(fixture).hasAttribute('open')).toBe(true);
    expect(painel(fixture).textContent).toContain('Escolher goleiro titular');
    http.expectOne(MARKET_URL).flush(
      mercado([
        item({ id: 'g2', name: 'Paredão', position: 'Goalkeeper', price: 9 }),
        item({ id: 'g3', name: 'Muralha', position: 'Goalkeeper', price: 4 }),
        item({
          id: 'g4',
          name: 'Gato',
          position: 'Goalkeeper',
          price: 30,
          blockCode: 'insufficient_balance',
          blockReason: 'Saldo insuficiente.',
        }),
        item({ id: 'g1', name: 'Pipoca', position: 'Goalkeeper', isOwned: true }),
        item({ id: 'a1', name: 'Bia', position: 'Midfielder' }),
      ]),
    );
    await fixture.whenStable();

    // Só goleiros fora do elenco; quem pode ser comprado vem antes, do mais barato.
    const nomes = [...painel(fixture).querySelectorAll('.opcao__nome')].map((nome) =>
      nome.textContent!.trim(),
    );
    expect(nomes).toEqual(['Muralha', 'Paredão', 'Gato']);
    expect(painel(fixture).textContent).toContain('Saldo insuficiente.');
    expect(botao(fixture, 'Comprar Gato').disabled).toBe(true);

    botao(fixture, 'Comprar Muralha').click();
    await fixture.whenStable();
    const pedido = http.expectOne(`/api/v1/fantasy/${SLUG}/squad/atleta/g3`);
    expect(pedido.request.method).toBe('POST');
    pedido.flush(
      visao({
        entry: entrada({
          balance: 96,
          slots: [
            vaga({ assetId: 'g3', name: 'Muralha', position: 'Goalkeeper' }),
            ...ELENCO.slice(1),
          ],
        }),
      }),
    );
    await fixture.whenStable();

    expect(painel(fixture).hasAttribute('open')).toBe(false);
    expect(texto(fixture)).toContain('Muralha entrou como titular. Saldo: C$ 96,00.');
    expect(vagas(fixture, 'button.ficha:not(.ficha--vazia)')).toContain(
      'Goleiro titular: Muralha, União da Vila, C$ 8,00',
    );
  });

  it('mostra quem está em cada vaga, com o capitão marcado por texto e não só por cor', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    expect(vagas(fixture, 'button.ficha:not(.ficha--vazia)')).toEqual([
      'Defensor titular: Baiano, União da Vila, C$ 8,00, capitão',
      'Defensor titular: Zeca, Estrela do Bairro, C$ 8,00',
      'Goleiro titular: Pipoca, União da Vila, C$ 8,00',
      'Reserva de defensor: Tanque, União da Vila, C$ 8,00',
      'Técnico: Seu Zé, União da Vila, C$ 11,00',
    ]);
    expect(elemento(fixture).querySelectorAll('button.ficha--vazia')).toHaveLength(7);
    expect(elemento(fixture).querySelector('.ficha--capitao .ficha__capitao')?.textContent).toBe(
      'C',
    );
  });

  it('escolhe o capitão pelo painel do atleta e anuncia o resultado', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Defensor titular: Zeca').click();
    await fixture.whenStable();
    expect(painel(fixture).hasAttribute('open')).toBe(true);
    expect(painel(fixture).textContent).toContain('Defensor titular');
    expect(painel(fixture).textContent).toContain('Estrela do Bairro · C$ 8,00');
    expect(painel(fixture).querySelector(`a[href="/c/${SLUG}/atletas/d2"]`)?.textContent).toContain(
      'Ver a página de Zeca',
    );

    botao(fixture, 'Tornar capitão').click();
    await fixture.whenStable();
    const pedido = http.expectOne(`${LINEUP_URL}/captain`);
    expect(pedido.request.method).toBe('PUT');
    expect(pedido.request.body).toEqual({ athleteId: 'd2' });
    pedido.flush(
      visao({
        entry: entrada({
          issues: [],
          slots: ELENCO.map((item) => ({ ...item, isCaptain: item.assetId === 'd2' })),
        }),
      }),
    );
    await fixture.whenStable();

    expect(painel(fixture).hasAttribute('open')).toBe(false);
    expect(texto(fixture)).toContain('Zeca é o capitão.');
    expect(texto(fixture)).toContain('Escalação completa. Ela vale para a Rodada 1 e congela');
    expect(texto(fixture)).toContain('(Horário de Brasília)');
  });

  it('o reserva entra no lugar de um titular da mesma posição', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Reserva de defensor: Tanque').click();
    await fixture.whenStable();
    expect(painel(fixture).textContent).not.toContain('Tornar capitão');
    botao(fixture, 'Entrar no lugar de Baiano').click();
    await fixture.whenStable();

    const pedido = http.expectOne(`${LINEUP_URL}/swap`);
    expect(pedido.request.body).toEqual({ starterAthleteId: 'd1', benchAthleteId: 'd3' });
    pedido.flush(visao({ entry: entrada({ slots: ELENCO }) }));
    await fixture.whenStable();

    // Baiano era o capitão: a braçadeira não vai com ele para o banco.
    expect(texto(fixture)).toContain(
      'Tanque entrou no lugar de Baiano, que foi para o banco. Escolha outro capitão.',
    );
  });

  it('vende pelo painel e mostra o novo saldo', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Técnico: Seu Zé').click();
    await fixture.whenStable();
    botao(fixture, 'Vender por C$ 11,00').click();
    await fixture.whenStable();

    const pedido = http.expectOne(`/api/v1/fantasy/${SLUG}/squad/tecnico/c1`);
    expect(pedido.request.method).toBe('DELETE');
    pedido.flush(visao({ entry: entrada({ balance: 61, slots: ELENCO.slice(0, 4) }) }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Seu Zé saiu do seu elenco. Saldo: C$ 61,00.');
    expect(vagas(fixture, 'button.ficha--vazia')).toContain('Escolher técnico');
  });

  it('recusa de regra fica no painel, junto da ação que a provocou', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Reserva de defensor: Tanque').click();
    await fixture.whenStable();
    botao(fixture, 'Entrar no lugar de Zeca').click();
    await fixture.whenStable();
    http.expectOne(`${LINEUP_URL}/swap`).flush(
      {
        title: 'Operação recusada',
        status: 409,
        detail: 'Limite do time: no máximo 3 titulares do mesmo time.',
        code: 'fantasy_team_starter_limit',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(painel(fixture).hasAttribute('open')).toBe(true);
    expect(painel(fixture).textContent).toContain(
      'Limite do time: no máximo 3 titulares do mesmo time.',
    );
  });

  it('mercado fechado no meio da ação fecha o painel e relê o campo', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Goleiro titular: Pipoca').click();
    await fixture.whenStable();
    botao(fixture, 'Tornar capitão').click();
    await fixture.whenStable();
    http.expectOne(`${LINEUP_URL}/captain`).flush(
      {
        title: 'Mercado fechado',
        status: 409,
        detail: 'O mercado está fechado.',
        code: 'fantasy_market_closed',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    http
      .expectOne(OVERVIEW_URL)
      .flush(visao({ market: MERCADO_FECHADO, entry: entrada({ slots: ELENCO }) }));
    await fixture.whenStable();

    expect(painel(fixture).hasAttribute('open')).toBe(false);
    expect(elemento(fixture).querySelectorAll('button.ficha')).toHaveLength(0);
    expect(texto(fixture)).not.toContain('Resumo da escalação');
    expect(texto(fixture)).toContain('Mercado fechado');
  });

  it('com o mercado fechado, mostra o retrato congelado com os nomes do fechamento', async () => {
    const fixture = await abrir(
      visao({
        market: MERCADO_FECHADO,
        entry: entrada({
          slots: ELENCO,
          lastClosedRound: fechada({
            captainAthleteId: 'g1',
            slots: [
              {
                kind: 'Athlete',
                assetId: 'g1',
                name: 'Pipoca (nome antigo)',
                position: 'Goalkeeper',
                realTeamId: 't1',
                realTeamName: 'União da Vila',
                role: 'Starter',
                price: 5,
                isCaptain: true,
              },
            ],
          }),
        }),
      }),
    );

    expect(texto(fixture)).toContain(
      'Escalação congelada para a Rodada 1 desde dom., 20/09 às 19:00 (Horário de Brasília).',
    );
    expect(vagas(fixture, '.ficha--leitura')).toContain(
      'Goleiro titular: Pipoca (nome antigo), União da Vila, C$ 5,00, capitão',
    );
    expect(texto(fixture)).not.toContain('Baiano');
    expect(elemento(fixture).querySelectorAll('button.ficha')).toHaveLength(0);
    expect(vagas(fixture, '.ficha--vazia')).toContain('Vaga vazia de técnico');
  });

  it('explica quando a escalação estava incompleta no fechamento', async () => {
    const fixture = await abrir(
      visao({
        market: MERCADO_FECHADO,
        entry: entrada({ slots: ELENCO, lastClosedRound: fechada({ status: 'Incomplete' }) }),
      }),
    );

    expect(texto(fixture)).toContain(
      'Sua escalação não estava completa quando o mercado da Rodada 1 fechou',
    );
    expect(vagas(fixture, '.ficha--leitura')).toContain(
      'Goleiro titular: Pipoca, União da Vila, C$ 8,00',
    );
  });

  it('explica a entrada depois do fechamento', async () => {
    const fixture = await abrir(
      visao({
        market: MERCADO_FECHADO,
        entry: entrada({ lastClosedRound: fechada({ status: 'JoinedAfterClose' }) }),
      }),
    );

    expect(texto(fixture)).toContain(
      'Você entrou depois do fechamento da Rodada 1. Seu jogo começa na próxima rodada',
    );
  });

  it('sem participação, aponta para a entrada no campeonato', async () => {
    const fixture = await abrir(visao({ entry: null }));

    expect(texto(fixture)).toContain('Para escalar, entre no campeonato primeiro');
    expect(
      elemento(fixture).querySelector(`a[href="/c/${SLUG}/jogar"].acao`)?.textContent,
    ).toContain('Entrar no campeonato');
    expect(elemento(fixture).querySelectorAll('.ficha')).toHaveLength(0);
  });

  it('mostra a mesma escalação em lista e lembra a escolha', async () => {
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO }) }));

    botao(fixture, 'Lista').click();
    await fixture.whenStable();

    expect(elemento(fixture).querySelector('.gramado')).toBeNull();
    expect(vagas(fixture, 'button.linha')).toContain(
      'Goleiro titular: Pipoca, União da Vila, C$ 8,00',
    );
    expect(texto(fixture)).toContain('Escolher reserva de goleiro');
    expect(botao(fixture, 'Lista').getAttribute('aria-pressed')).toBe('true');
    expect(globalThis.localStorage?.getItem('cv.escalacao-modo')).toBe('lista');
  });

  it('anuncia o que entrou quando volta do mercado completo', async () => {
    TestBed.inject(FantasyNotice).deixar('Pipoca entrou como titular. Saldo: C$ 95,00.');
    const fixture = await abrir(visao({ entry: entrada({ slots: ELENCO.slice(0, 1) }) }));

    expect(texto(fixture)).toContain('Pipoca entrou como titular. Saldo: C$ 95,00.');
    expect(TestBed.inject(FantasyNotice).retirar()).toBeNull();
  });
});
