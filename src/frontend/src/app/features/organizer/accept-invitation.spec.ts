import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { Account, AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { AcceptInvitationPage } from './accept-invitation';
import { PendingInvitation } from './invitation.service';
import { OrganizerArea } from './organizer-area';

const ACCEPT = '/api/v1/organization-invitations/accept';

const CONTA: Account = {
  id: 'u1',
  email: 'auxiliar@exemplo.local',
  displayName: 'Ana Auxiliar',
  emailConfirmed: true,
  roles: [],
};

async function estabilizar(fixture: ComponentFixture<AcceptInvitationPage>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

describe('AcceptInvitationPage', () => {
  let http: HttpTestingController;
  let conta: ReturnType<typeof signal<Account | null>>;
  let area: { reload: ReturnType<typeof vi.fn> };
  let queryParams: Record<string, string>;

  beforeEach(() => {
    conta = signal<Account | null>(CONTA);
    area = { reload: vi.fn() };
    queryParams = { token: 'token-do-email' };

    TestBed.configureTestingModule({
      imports: [AcceptInvitationPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        { provide: AuthService, useValue: { current: conta, logout: vi.fn() } },
        { provide: OrganizerArea, useValue: area },
        {
          provide: ActivatedRoute,
          useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(queryParams) } }),
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(): Promise<{
    fixture: ComponentFixture<AcceptInvitationPage>;
    texto: () => string;
  }> {
    const fixture = TestBed.createComponent(AcceptInvitationPage);
    await fixture.whenStable();
    const elemento = fixture.nativeElement as HTMLElement;
    return { fixture, texto: () => elemento.textContent ?? '' };
  }

  function aceitar(fixture: ComponentFixture<AcceptInvitationPage>): void {
    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.trim() === 'Aceitar convite',
    );
    expect(botao, 'Botão "Aceitar convite" não encontrado').toBeDefined();
    botao!.click();
  }

  it('retira o token da URL assim que abre, sem chamar a API', async () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const { texto } = await abrir();

    expect(navegar).toHaveBeenCalledWith([], { queryParams: {}, replaceUrl: true });
    expect(TestBed.inject(PendingInvitation).current()).toBe('token-do-email');
    expect(texto()).toContain('auxiliar@exemplo.local');
    http.expectNone(ACCEPT);
  });

  it('sem sessão, pede para entrar sem levar o token para o destino do login', async () => {
    conta.set(null);

    const { fixture, texto } = await abrir();

    expect(texto()).toContain('Entre para aceitar');
    const entrar = (fixture.nativeElement as HTMLElement).querySelector('a[href^="/entrar"]');
    expect(entrar?.getAttribute('href')).toBe('/entrar?destino=%2Forganizar%2Fconvite');
  });

  it('depois do login, a tela sem token na URL usa o convite guardado na memória', async () => {
    TestBed.inject(PendingInvitation).keep('guardado-antes-do-login');
    queryParams = {};

    const { fixture } = await abrir();
    aceitar(fixture);
    await estabilizar(fixture);

    expect(http.expectOne(ACCEPT).request.body).toEqual({ token: 'guardado-antes-do-login' });
  });

  it('aceite concluído mostra o resultado e descarta o token', async () => {
    const { fixture, texto } = await abrir();

    aceitar(fixture);
    await estabilizar(fixture);
    http.expectOne(ACCEPT).flush({});
    await estabilizar(fixture);

    expect(texto()).toContain('Convite aceito');
    expect(texto()).not.toContain('Não encontramos o convite');
    expect(TestBed.inject(PendingInvitation).current()).toBeNull();
    // Quem acabou de virar auxiliar passa a ver a área de organização na navegação.
    expect(area.reload).toHaveBeenCalledOnce();
  });

  it('conta com outro e-mail recebe orientação e o token continua guardado', async () => {
    const { fixture, texto } = await abrir();

    aceitar(fixture);
    await estabilizar(fixture);
    http
      .expectOne(ACCEPT)
      .flush({}, new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    await estabilizar(fixture);

    expect(texto()).toContain('enviado para outro e-mail');
    expect(texto()).toContain('Entrar com outra conta');
    expect(TestBed.inject(PendingInvitation).current()).toBe('token-do-email');
  });

  it('conta de demonstração vê o motivo do bloqueio, não a mensagem de outro e-mail', async () => {
    const { fixture, texto } = await abrir();

    aceitar(fixture);
    await estabilizar(fixture);
    http
      .expectOne(ACCEPT)
      .flush(
        { status: 403, title: 'Modo demonstração', code: 'demo_read_only' },
        new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }),
      );
    await estabilizar(fixture);

    expect(texto()).toContain('conta de demonstração');
    expect(texto()).not.toContain('enviado para outro e-mail');
  });

  it.each([
    [410, 'Este convite expirou'],
    [409, 'não pode mais ser usado'],
    [404, 'Convite não encontrado'],
  ])('resposta %i vira mensagem definitiva sem novo botão de aceite', async (status, mensagem) => {
    const { fixture, texto } = await abrir();

    aceitar(fixture);
    await estabilizar(fixture);
    http.expectOne(ACCEPT).flush({}, new HttpErrorResponse({ status, statusText: 'Erro' }));
    await estabilizar(fixture);

    expect(texto()).toContain(mensagem);
    expect(texto()).not.toContain('Aceitar convite');
  });

  it('endereço sem token explica como abrir o link de novo', async () => {
    queryParams = {};

    const { texto } = await abrir();

    expect(texto()).toContain('Não encontramos o convite');
  });
});
