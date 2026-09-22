import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { Account, AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { NotificationInbox } from '../../core/notifications/notification.service';
import { NotificationBell } from './notification-bell';

const URL = '/api/v1/notifications';
const READ_URL = '/api/v1/notifications/read';

function caixa(changes: Partial<NotificationInbox> = {}): NotificationInbox {
  return {
    unread: 1,
    items: [
      {
        id: 'n1',
        kind: 'RoundPublished',
        title: 'Rodada 1 apurada',
        body: 'O resultado saiu em Copa da Vila. Veja quanto você fez.',
        competitionSlug: 'copa-da-vila',
        roundId: 'r1',
        createdAt: '2026-09-22T12:00:00Z',
        read: false,
      },
    ],
    ...changes,
  };
}

const PESSOA: Account = {
  id: 'u1',
  displayName: 'Pessoa Pontuada',
  email: 'pessoa@exemplo.test',
  emailConfirmed: true,
  roles: [],
};

describe('NotificationBell', () => {
  let http: HttpTestingController;
  let conta: ReturnType<typeof signal<Account | null>>;

  beforeEach(() => {
    conta = signal<Account | null>(PESSOA);
    TestBed.configureTestingModule({
      imports: [NotificationBell],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        { provide: AuthService, useValue: { current: conta } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrirSino(
    inicial: NotificationInbox,
  ): Promise<ComponentFixture<NotificationBell>> {
    const fixture = TestBed.createComponent(NotificationBell);
    await fixture.whenStable();
    http.expectOne(URL).flush(inicial);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<NotificationBell>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(fixture: ComponentFixture<NotificationBell>): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector('button')!;
  }

  it('mostra quantos avisos estão por ler sem abrir a lista', async () => {
    const fixture = await abrirSino(caixa({ unread: 3 }));

    expect(botao(fixture).getAttribute('aria-label')).toBe('Avisos, 3 por ler');
    expect(botao(fixture).getAttribute('aria-expanded')).toBe('false');
    expect(texto(fixture)).toContain('3');
    expect(texto(fixture)).not.toContain('Rodada 1 apurada');
  });

  it('abrir a caixa é ler: a lista aparece e o contador zera', async () => {
    const fixture = await abrirSino(caixa());

    botao(fixture).click();
    await fixture.whenStable();
    http.expectOne(URL).flush(caixa());
    const marcadas = http.expectOne(READ_URL);
    expect(marcadas.request.method).toBe('POST');
    marcadas.flush(caixa({ unread: 0, items: [{ ...caixa().items[0], read: true }] }));
    await fixture.whenStable();

    expect(botao(fixture).getAttribute('aria-expanded')).toBe('true');
    expect(texto(fixture)).toContain('Rodada 1 apurada');
    expect(texto(fixture)).toContain('O resultado saiu em Copa da Vila');
    expect(botao(fixture).getAttribute('aria-label')).toBe('Avisos');
  });

  it('o aviso da rodada leva à pontuação dela', async () => {
    const fixture = await abrirSino(caixa());

    botao(fixture).click();
    await fixture.whenStable();
    http.expectOne(URL).flush(caixa());
    http.expectOne(READ_URL).flush(caixa({ unread: 0 }));
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a')!;
    expect(link.getAttribute('href')).toBe('/c/copa-da-vila/pontuacao/r1');
  });

  it('a caixa vazia explica para que o sino serve', async () => {
    const fixture = await abrirSino({ unread: 0, items: [] });

    botao(fixture).click();
    await fixture.whenStable();
    http.expectOne(URL).flush({ unread: 0, items: [] });
    await fixture.whenStable();

    // Sem nada por ler, marcar como lido seria uma ida ao servidor à toa.
    http.expectNone(READ_URL);
    expect(texto(fixture)).toContain('Os resultados das suas rodadas aparecem neste sino');
  });

  it('falha ao carregar não derruba a página: o sino fica vazio e calado', async () => {
    const fixture = await abrirSino(caixa());
    expect(texto(fixture)).toContain('1');

    botao(fixture).click();
    await fixture.whenStable();
    http.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne(READ_URL).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(botao(fixture).getAttribute('aria-label')).toBe('Avisos');
    expect(texto(fixture)).toContain('Nada por aqui ainda');
  });

  it('sem sessão o sino não existe', async () => {
    conta.set(null);
    const fixture = TestBed.createComponent(NotificationBell);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();
  });
});
