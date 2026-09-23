import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CODIGO, LEAGUES_URL, LIGA_ID, SLUG, resumo } from './league-fixtures';
import { FantasyLeaguesPage } from './fantasy-leagues';
import { LeagueSummary } from './league.service';

describe('FantasyLeaguesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FantasyLeaguesPage],
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

  async function abrir(ligas: LeagueSummary[]): Promise<ComponentFixture<FantasyLeaguesPage>> {
    const fixture = TestBed.createComponent(FantasyLeaguesPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(LEAGUES_URL).flush(ligas);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<FantasyLeaguesPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function clicar(fixture: ComponentFixture<FantasyLeaguesPage>, rotulo: string): void {
    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.trim().startsWith(rotulo),
    );
    botao!.click();
  }

  function digitar(fixture: ComponentFixture<FantasyLeaguesPage>, valor: string): void {
    const campo = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  it('lista as ligas da conta com a colocação em cada uma', async () => {
    const fixture = await abrir([
      resumo({ members: 12, position: 3 }),
      resumo({ id: 'outra', name: 'Copa dos amigos', members: 1, position: null, isOwner: true }),
    ]);

    expect(texto(fixture)).toContain('Turma do sábado');
    expect(texto(fixture)).toContain('12 participantes');
    expect(texto(fixture)).toContain('você está em 3º');
    expect(texto(fixture)).toContain('1 participante ·');
    expect(texto(fixture)).toContain('liga criada por você');
    // Sem rodada apurada não existe colocação, e a tela não inventa uma.
    expect(texto(fixture)).not.toContain('você está em 1º');

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      `a.liga[href="/c/${SLUG}/ligas/${LIGA_ID}"]`,
    );
    expect(link?.textContent).toContain('Turma do sábado');
  });

  it('explica o que é uma liga para quem não está em nenhuma', async () => {
    const fixture = await abrir([]);

    expect(texto(fixture)).toContain('Você ainda não está em nenhuma liga');
    expect(texto(fixture)).toContain('recorte do ranking entre pessoas que você conhece');
    expect(texto(fixture)).toContain('A pontuação é a mesma do campeonato');
  });

  it('cria a liga e abre a liga nova, onde está o código', async () => {
    const fixture = await abrir([]);
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    clicar(fixture, 'Criar uma liga');
    await fixture.whenStable();
    digitar(fixture, 'Turma do sábado');
    clicar(fixture, 'Criar liga');
    await fixture.whenStable();

    const pedido = http.expectOne(LEAGUES_URL);
    expect(pedido.request.method).toBe('POST');
    expect(pedido.request.body).toEqual({ name: 'Turma do sábado' });
    pedido.flush(resumo({ isOwner: true, inviteCode: CODIGO }));
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'ligas', LIGA_ID]);
  });

  it('recusa nome curto demais antes de gastar uma ida ao servidor', async () => {
    const fixture = await abrir([]);

    clicar(fixture, 'Criar uma liga');
    await fixture.whenStable();
    digitar(fixture, 'ab');
    clicar(fixture, 'Criar liga');
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Use de 3 a 60 caracteres no nome.');
    http.expectNone(LEAGUES_URL);
  });

  it('entra pelo código aceitando hífens e minúsculas, como ele chega no grupo', async () => {
    const fixture = await abrir([]);
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    clicar(fixture, 'Entrar com um código');
    await fixture.whenStable();
    digitar(fixture, 'abcde-2345k');
    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();

    const pedido = http.expectOne('/api/v1/league-invites/abcde-2345k/accept');
    expect(pedido.request.method).toBe('POST');
    pedido.flush(resumo());
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'ligas', LIGA_ID]);
  });

  it('confere o tamanho do código antes de enviar', async () => {
    const fixture = await abrir([]);

    clicar(fixture, 'Entrar com um código');
    await fixture.whenStable();
    digitar(fixture, 'ABC-123');
    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();

    expect(texto(fixture)).toContain('O código tem 10 letras e números.');
  });

  it('código recusado lista os motivos possíveis, sem confirmar que a liga existe', async () => {
    const fixture = await abrir([]);

    clicar(fixture, 'Entrar com um código');
    await fixture.whenStable();
    digitar(fixture, CODIGO);
    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();

    http
      .expectOne(`/api/v1/league-invites/${CODIGO}/accept`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Este código não vale');
    expect(texto(fixture)).toContain('pode ter sido trocado');
    expect(texto(fixture)).toContain('campeonato que você ainda não joga');
  });

  it('avisa quando a liga está cheia', async () => {
    const fixture = await abrir([]);

    clicar(fixture, 'Entrar com um código');
    await fixture.whenStable();
    digitar(fixture, CODIGO);
    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();

    http
      .expectOne(`/api/v1/league-invites/${CODIGO}/accept`)
      .flush(
        { title: 'Liga cheia', status: 409, code: 'league_member_limit' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Esta liga já está cheia');
  });

  it('liga os botões ao painel que eles abrem', async () => {
    const fixture = await abrir([]);
    const elemento = fixture.nativeElement as HTMLElement;

    const criar = [...elemento.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Criar uma liga'),
    )!;
    expect(criar.getAttribute('aria-expanded')).toBe('false');
    expect(criar.getAttribute('aria-controls')).toBe('painel-liga');

    criar.click();
    await fixture.whenStable();

    expect(criar.getAttribute('aria-expanded')).toBe('true');
    expect(elemento.querySelector('#painel-liga')).not.toBeNull();
  });

  it('não confunde campeonato inexistente com erro de rede', async () => {
    const fixture = TestBed.createComponent(FantasyLeaguesPage);
    fixture.componentRef.setInput('campeonato', SLUG);
    await fixture.whenStable();
    http.expectOne(LEAGUES_URL).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Não encontramos este campeonato');
  });
});
