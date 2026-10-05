import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { LoginPage } from './login';

describe('LoginPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LoginPage],
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

  /** A inicialização já sabe se a demonstração liga a entrada de visitante. */
  async function abrir(demo = false, selfService = true): Promise<ComponentFixture<LoginPage>> {
    const consulta = TestBed.inject(AuthService).loadDemoAccess();
    http.expectOne('/api/v1/auth/demo').flush({ available: demo, selfService });
    await consulta;

    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    return fixture;
  }

  it('diz antes do formulário que é fantasy e que não há aposta em dinheiro', async () => {
    const fixture = await abrir();
    const elemento = fixture.nativeElement as HTMLElement;

    const selo = elemento.querySelector('.selo');
    expect(selo?.textContent).toContain('Fantasy de futebol amador');
    expect(selo?.textContent).toContain('Sem apostas em dinheiro');
    expect(
      selo!.compareDocumentPosition(elemento.querySelector('form')!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('oferece criar conta e recuperar senha só quando a publicação tem e-mail', async () => {
    const comEmail = (await abrir()).nativeElement as HTMLElement;
    expect(comEmail.querySelector('a[href="/cadastro"]')).not.toBeNull();
    expect(comEmail.querySelector('a[href="/recuperar-senha"]')).not.toBeNull();

    const semEmail = (await abrir(true, false)).nativeElement as HTMLElement;
    expect(semEmail.querySelector('a[href="/cadastro"]')).toBeNull();
    expect(semEmail.querySelector('a[href="/recuperar-senha"]')).toBeNull();
    expect(semEmail.textContent).toContain('Entrar como visitante');
  });

  it('marca os campos para gerenciadores de senha', async () => {
    const fixture = await abrir();

    const elemento = fixture.nativeElement as HTMLElement;
    const usuario = elemento.querySelector('input[autocomplete="username"]');
    const senha = elemento.querySelector('input[autocomplete="current-password"]');

    expect(usuario).not.toBeNull();
    expect(senha).not.toBeNull();
    expect(senha?.getAttribute('type')).toBe('password');
  });

  it('mostra a mensagem genérica da API sem revelar se a conta existe', async () => {
    const fixture = await abrir();

    const form = (fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    await new Promise((resolve) => setTimeout(resolve, 0));

    http
      .expectOne('/api/v1/auth/login')
      .flush(
        { status: 401, title: 'E-mail ou senha incorretos' },
        new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' }),
      );
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('Entre na sua conta para continuar');
    // Nada na tela diferencia "não existe" de "senha errada".
    expect(texto).not.toContain('não existe');
    expect(texto).not.toContain('não encontrada');
  });

  it('cada campo tem rótulo persistente associado ao input', async () => {
    const fixture = await abrir();

    const elemento = fixture.nativeElement as HTMLElement;
    const rotulos = [...elemento.querySelectorAll('label')];

    expect(rotulos.length).toBe(2);
    for (const rotulo of rotulos) {
      const alvo = elemento.querySelector(`#${rotulo.getAttribute('for')}`);
      expect(alvo).not.toBeNull();
    }
  });

  it('fora da demonstração não oferece a entrada de visitante', async () => {
    const fixture = await abrir(false);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
      'Entrar como visitante',
    );
  });

  it('na demonstração, o visitante entra sem senha e vai para o início', async () => {
    const fixture = await abrir(true);
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const botao = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.includes('Entrar como visitante'),
    )!;
    botao.click();
    await new Promise((resolve) => setTimeout(resolve, 0));

    http
      .expectOne({ method: 'POST', url: '/api/v1/auth/demo' })
      .flush(null, { status: 204, statusText: 'No Content' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/v1/auth/antiforgery').flush(null);
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/v1/auth/me').flush({
      id: 'u1',
      email: 'visitante@demo.test',
      displayName: 'Visitante',
      emailConfirmed: true,
      roles: ['DemoViewer'],
    });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(navegar).toHaveBeenCalledWith('/inicio');
  });
});
