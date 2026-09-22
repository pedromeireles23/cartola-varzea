import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CODIGO, LEAGUE_URL, LIGA_ID, SLUG, liga, membro } from './league-fixtures';
import { FantasyLeaguePage } from './fantasy-league';
import { League } from './league.service';

const MEMBERSHIP = '22222222-2222-2222-2222-222222222222';

describe('FantasyLeaguePage', () => {
  let http: HttpTestingController;

  // O jsdom nao implementa <dialog>; o mesmo remendo das outras telas com dialogo.
  beforeAll(() => {
    const prototipo = HTMLDialogElement.prototype as HTMLDialogElement & {
      showModal?: () => void;
    };
    prototipo.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    prototipo.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyLeaguePage],
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

  async function abrir(dados: League): Promise<ComponentFixture<FantasyLeaguePage>> {
    const fixture = TestBed.createComponent(FantasyLeaguePage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('ligaId', LIGA_ID);
    await fixture.whenStable();
    http.expectOne(LEAGUE_URL).flush(dados);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<FantasyLeaguePage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(
    fixture: ComponentFixture<FantasyLeaguePage>,
    rotulo: string,
  ): HTMLButtonElement | undefined {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((button) =>
      button.textContent?.trim().startsWith(rotulo),
    );
  }

  function clicar(fixture: ComponentFixture<FantasyLeaguePage>, rotulo: string): void {
    botao(fixture, rotulo)!.click();
  }

  /** Confirmar tem o mesmo rotulo do gatilho em duas acoes; aqui so vale o do dialogo. */
  function confirmar(fixture: ComponentFixture<FantasyLeaguePage>, rotulo: string): void {
    const dialogo = (fixture.nativeElement as HTMLElement).querySelector('dialog')!;
    [...dialogo.querySelectorAll('button')]
      .find((button) => button.textContent?.trim().startsWith(rotulo))!
      .click();
  }

  it('mostra a classificação da liga com o mesmo detalhe do ranking geral', async () => {
    const fixture = await abrir(
      liga({
        members: [
          membro({ position: 1, displayName: 'Pessoa Um', isOwner: true }),
          membro({
            position: 2,
            displayName: 'Pessoa Dois',
            totalPoints: 30,
            netWorth: 98,
            lastRoundPoints: null,
            isViewer: true,
            membershipId: MEMBERSHIP,
          }),
        ],
      }),
    );

    expect(texto(fixture)).toContain('Turma do sábado');
    expect(texto(fixture)).toContain('2 participantes · Copa da Vila');
    expect(texto(fixture)).toContain('2 rodadas apuradas, até Rodada 2.');
    expect(texto(fixture)).toContain('46,00 pts');
    expect(texto(fixture)).toContain('C$ 104,50');
    expect(texto(fixture)).toContain('16,00 pts na última');
    expect(texto(fixture)).toContain('não jogou a última');
    expect(texto(fixture)).toContain('Dono');
    expect(texto(fixture)).toContain('Você');
    expect((fixture.nativeElement as HTMLElement).querySelector('.linha--voce')).not.toBeNull();
  });

  it('avisa que a classificação pode mudar enquanto a última rodada é provisória', async () => {
    const fixture = await abrir(liga({ provisional: true }));

    expect(texto(fixture)).toContain('Rodada 2 ainda é provisória');
  });

  it('liga sem rodada apurada explica que a classificação ainda não se mexeu', async () => {
    const fixture = await abrir(liga({ rounds: 0, lastRoundName: null }));

    expect(texto(fixture)).toContain('Nenhuma rodada foi apurada ainda');
    expect(texto(fixture)).not.toContain('na última');
  });

  it('o dono vê o código agrupado e o copia', async () => {
    const escrever = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: escrever },
      configurable: true,
    });

    const fixture = await abrir(liga({ isOwner: true, inviteCode: CODIGO }));

    expect(texto(fixture)).toContain('ABCDE-2345K');
    expect(texto(fixture)).toContain('dá para reenviar ao grupo semanas depois');

    clicar(fixture, 'Copiar código');
    await fixture.whenStable();

    expect(escrever).toHaveBeenCalledWith(CODIGO);
    expect(texto(fixture)).toContain('Copiado');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.convite__codigo--copiado'),
    ).not.toBeNull();
  });

  it('navegador sem área de transferência pede a cópia manual em vez de mentir', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });

    const fixture = await abrir(liga({ isOwner: true, inviteCode: CODIGO }));
    clicar(fixture, 'Copiar código');
    await fixture.whenStable();

    expect(texto(fixture)).toContain('selecione o código acima e copie');
    expect(texto(fixture)).not.toContain('Copiado');
  });

  it('trocar o código avisa que o antigo para de valer antes de trocar', async () => {
    const fixture = await abrir(liga({ isOwner: true, inviteCode: CODIGO }));

    clicar(fixture, 'Trocar o código');
    await fixture.whenStable();
    expect(texto(fixture)).toContain('O código de agora para de funcionar na hora');

    confirmar(fixture, 'Trocar o código');
    await fixture.whenStable();

    const pedido = http.expectOne(`${LEAGUE_URL}/invite`);
    expect(pedido.request.method).toBe('PUT');
    expect(pedido.request.body).toEqual({ version: 'v1', close: false });
    pedido.flush({});
    await fixture.whenStable();
    http.expectOne(LEAGUE_URL).flush(liga({ isOwner: true, inviteCode: 'ZYXWV9876' }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Código novo gerado.');
  });

  it('liga fechada para entradas oferece gerar um código novo', async () => {
    const fixture = await abrir(liga({ isOwner: true, inviteCode: null }));

    expect(texto(fixture)).toContain('A liga está fechada para novas entradas');

    clicar(fixture, 'Gerar um código novo');
    await fixture.whenStable();

    const pedido = http.expectOne(`${LEAGUE_URL}/invite`);
    expect(pedido.request.body).toEqual({ version: 'v1', close: false });
    pedido.flush({});
    await fixture.whenStable();
    http.expectOne(LEAGUE_URL).flush(liga({ isOwner: true, inviteCode: CODIGO }));
    await fixture.whenStable();
  });

  it('o ranking fica limpo até o dono entrar no modo de gestão', async () => {
    const fixture = await abrir(
      liga({
        isOwner: true,
        members: [
          membro({ position: 1, displayName: 'Dona', isViewer: true, isOwner: true }),
          membro({ position: 2, displayName: 'Convidado', membershipId: MEMBERSHIP }),
        ],
      }),
    );

    expect(botao(fixture, 'Remover')).toBeUndefined();

    clicar(fixture, 'Gerenciar participantes');
    await fixture.whenStable();

    expect(botao(fixture, 'Remover')).toBeDefined();
    // O dono não se remove pela lista: para sair da liga ele a apaga.
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.linha__acao')).toHaveLength(1);

    clicar(fixture, 'Remover');
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Remover Convidado da liga?');

    confirmar(fixture, 'Remover');
    await fixture.whenStable();

    const pedido = http.expectOne(`${LEAGUE_URL}/members/${MEMBERSHIP}`);
    expect(pedido.request.method).toBe('DELETE');
    pedido.flush(null);
    await fixture.whenStable();
    http.expectOne(LEAGUE_URL).flush(liga({ isOwner: true }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Convidado saiu da liga.');
  });

  it('apagar a liga confirma, manda a versão lida e volta para a lista', async () => {
    const fixture = await abrir(liga({ isOwner: true, inviteCode: CODIGO }));
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    clicar(fixture, 'Apagar esta liga');
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Apagar a liga Turma do sábado?');
    expect(texto(fixture)).toContain('não pode ser desfeito');

    clicar(fixture, 'Apagar a liga');
    await fixture.whenStable();

    const pedido = http.expectOne(`${LEAGUE_URL}?version=v1`);
    expect(pedido.request.method).toBe('DELETE');
    pedido.flush(null);
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'ligas']);
  });

  it('quem não é dono sai da liga pela própria associação', async () => {
    const fixture = await abrir(
      liga({
        members: [
          membro({ position: 1, displayName: 'Dona', isOwner: true }),
          membro({
            position: 2,
            displayName: 'Eu',
            isViewer: true,
            membershipId: MEMBERSHIP,
          }),
        ],
      }),
    );
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    // Sem cartão de convite nem de apagar: nada disso é dele.
    expect(texto(fixture)).not.toContain('Convite');
    expect(texto(fixture)).not.toContain('Apagar esta liga');

    clicar(fixture, 'Sair desta liga');
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Sair da liga Turma do sábado?');

    clicar(fixture, 'Sair da liga');
    await fixture.whenStable();

    http.expectOne(`${LEAGUE_URL}/members/${MEMBERSHIP}`).flush(null);
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'ligas']);
  });

  it('quem não é membro recebe o mesmo que quem procurou liga inexistente', async () => {
    const fixture = TestBed.createComponent(FantasyLeaguePage);
    fixture.componentRef.setInput('campeonato', SLUG);
    fixture.componentRef.setInput('ligaId', LIGA_ID);
    await fixture.whenStable();
    http.expectOne(LEAGUE_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos esta liga');
  });

  it('liga alterada por outra pessoa não perde o que está na tela', async () => {
    const fixture = await abrir(liga({ isOwner: true, inviteCode: CODIGO }));

    clicar(fixture, 'Fechar para novas entradas');
    await fixture.whenStable();
    clicar(fixture, 'Fechar a liga');
    await fixture.whenStable();

    http.expectOne(`${LEAGUE_URL}/invite`).flush(null, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Alguém alterou esses dados');
    expect(texto(fixture)).toContain('ABCDE-2345K');
  });
});
