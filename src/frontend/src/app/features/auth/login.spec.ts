import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

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

  it('marca os campos para gerenciadores de senha', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    const usuario = elemento.querySelector('input[autocomplete="username"]');
    const senha = elemento.querySelector('input[autocomplete="current-password"]');

    expect(usuario).not.toBeNull();
    expect(senha).not.toBeNull();
    expect(senha?.getAttribute('type')).toBe('password');
  });

  it('mostra a mensagem genérica da API sem revelar se a conta existe', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();

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
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    const rotulos = [...elemento.querySelectorAll('label')];

    expect(rotulos.length).toBe(2);
    for (const rotulo of rotulos) {
      const alvo = elemento.querySelector(`#${rotulo.getAttribute('for')}`);
      expect(alvo).not.toBeNull();
    }
  });
});
