import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CODIGO, LEAGUE_URL, LIGA_ID, SLUG, liga, membro, resumo } from './league-fixtures';
import { LeagueInvitePage } from './league-invite';

const ACEITAR = `/api/v1/league-invites/${CODIGO}/accept`;
const MEMBERSHIP = '33333333-3333-3333-3333-333333333333';

describe('LeagueInvitePage', () => {
  let http: HttpTestingController;
  let logado: boolean;

  beforeEach(() => {
    logado = true;
    TestBed.configureTestingModule({
      imports: [LeagueInvitePage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        { provide: AuthService, useValue: { isAuthenticated: () => logado } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(codigo = CODIGO): Promise<ComponentFixture<LeagueInvitePage>> {
    const fixture = TestBed.createComponent(LeagueInvitePage);
    fixture.componentRef.setInput('codigo', codigo);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<LeagueInvitePage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function clicar(fixture: ComponentFixture<LeagueInvitePage>, rotulo: string): void {
    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.trim().startsWith(rotulo),
    );
    botao!.click();
  }

  it('entra sozinha ao abrir e diz em que liga a pessoa entrou', async () => {
    const fixture = await abrir();

    const pedido = http.expectOne(ACEITAR);
    expect(pedido.request.method).toBe('POST');
    pedido.flush(resumo({ members: 5 }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Você entrou na liga');
    expect(texto(fixture)).toContain('Agora você disputa a Turma do sábado.');
    expect(texto(fixture)).toContain('5 participantes na liga');
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.acao');
    expect(link?.getAttribute('href')).toBe(`/c/${SLUG}/ligas/${LIGA_ID}`);
  });

  it('quem entrou sem querer desfaz na mesma tela', async () => {
    const fixture = await abrir();
    http.expectOne(ACEITAR).flush(resumo());
    await fixture.whenStable();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect(texto(fixture)).toContain('Não era o que você queria?');
    clicar(fixture, 'Sair desta liga');
    await fixture.whenStable();

    // O resumo do convite não traz o membershipId: ele só vem na liga aberta.
    http
      .expectOne(LEAGUE_URL)
      .flush(liga({ members: [membro({ isViewer: true, membershipId: MEMBERSHIP })] }));
    await fixture.whenStable();
    const saida = http.expectOne(`${LEAGUE_URL}/members/${MEMBERSHIP}`);
    expect(saida.request.method).toBe('DELETE');
    saida.flush(null);
    await fixture.whenStable();

    // Sair e ficar no convite faria a entrada automática disparar de novo.
    expect(navegar).toHaveBeenCalledWith(['/c', SLUG, 'ligas']);
  });

  it('link truncado não vira tentativa: o limite por conta é curto', async () => {
    const fixture = await abrir('ABCDE');

    http.expectNone(ACEITAR);
    expect(texto(fixture)).toContain('não parece ter um código completo');
    const entrar = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.includes('Entrar na liga'),
    );
    expect(entrar?.disabled).toBe(true);
  });

  it('sem sessão, não entra sozinha: a decisão volta para a pessoa', async () => {
    logado = false;
    const fixture = await abrir();

    http.expectNone(ACEITAR);
    expect(texto(fixture)).toContain('Você foi convidado');
    expect(texto(fixture)).toContain('ABCDE-2345K');
  });

  it('convite que não vale lista os motivos sem confirmar que a liga existe', async () => {
    const fixture = await abrir();
    http.expectOne(ACEITAR).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Este código não vale');
    expect(texto(fixture)).toContain('fechada para novas entradas');
  });

  it('depois de uma recusa, tentar de novo é decisão de quem está ali', async () => {
    const fixture = await abrir();
    http.expectOne(ACEITAR).flush(null, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Muitas tentativas em pouco tempo');

    // Voltar ao convite não dispara nada sozinho: um 429 repetido viraria laço.
    clicar(fixture, 'Tentar de novo');
    await fixture.whenStable();
    http.expectNone(ACEITAR);
    expect(texto(fixture)).toContain('Você foi convidado');

    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();
    http.expectOne(ACEITAR).flush(resumo());
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Agora você disputa a Turma do sábado.');
  });
});
