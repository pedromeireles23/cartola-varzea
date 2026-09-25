import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
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

  /** A tela pergunta se a demonstração liga a entrada de visitante. */
  async function abrir(demo = false): Promise<ComponentFixture<LoginPage>> {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    http.expectOne('/api/v1/auth/demo').flush({ available: demo });
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    return fixture;
  }

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
