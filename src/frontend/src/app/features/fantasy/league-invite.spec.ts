import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { CODIGO, LIGA_ID, SLUG, resumo } from './league-fixtures';
import { LeagueInvitePage } from './league-invite';

const ACEITAR = `/api/v1/league-invites/${CODIGO}/accept`;

describe('LeagueInvitePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LeagueInvitePage],
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

  it('não entra sozinha ao abrir: entrar é uma decisão da pessoa', async () => {
    const fixture = await abrir();

    http.expectNone(ACEITAR);
    expect(texto(fixture)).toContain('Você foi convidado');
    expect(texto(fixture)).toContain('ABCDE-2345K');
    expect(texto(fixture)).toContain('mesmo elenco, mesma pontuação, mesmas rodadas');
  });

  it('entra na liga e leva para a classificação dela', async () => {
    const fixture = await abrir();

    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();

    const pedido = http.expectOne(ACEITAR);
    expect(pedido.request.method).toBe('POST');
    pedido.flush(resumo({ members: 5 }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Você entrou na liga Turma do sábado.');
    expect(texto(fixture)).toContain('5 participantes na liga');
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.acao');
    expect(link?.getAttribute('href')).toBe(`/c/${SLUG}/ligas/${LIGA_ID}`);
  });

  it('link truncado é barrado antes de virar uma tentativa de código', async () => {
    const fixture = await abrir('ABCDE');

    expect(texto(fixture)).toContain('não parece ter um código completo');
    const entrar = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.includes('Entrar na liga'),
    );
    expect(entrar?.disabled).toBe(true);
  });

  it('convite que não vale lista os motivos sem confirmar que a liga existe', async () => {
    const fixture = await abrir();

    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();
    http.expectOne(ACEITAR).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Este código não vale');
    expect(texto(fixture)).toContain('fechada para novas entradas');
  });

  it('muitas tentativas em pouco tempo recebem o aviso do limite', async () => {
    const fixture = await abrir();

    clicar(fixture, 'Entrar na liga');
    await fixture.whenStable();
    http.expectOne(ACEITAR).flush(null, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Muitas tentativas em pouco tempo');
  });
});
