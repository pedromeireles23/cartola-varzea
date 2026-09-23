import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api-base-url';
import { Shell } from '../shell/shell';

/** A conta de demonstração sempre tem sessão: é uma conta pública somente leitura. */
const CONTA = {
  id: 'conta-demo',
  email: 'demo@exemplo.local',
  displayName: 'Visitante',
  emailConfirmed: true,
  roles: ['DemoViewer'],
};

describe('Faixa de modo demonstração', () => {
  function montar(demo: boolean): HTMLElement {
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        {
          provide: AuthService,
          useValue: {
            current: signal({ ...CONTA, roles: demo ? ['DemoViewer'] : [] }),
            isDemoViewer: signal(demo),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('aparece na casca para a conta de demonstração, com texto e região nomeada', () => {
    const elemento = montar(true);

    const faixa = elemento.querySelector('aside[aria-label="Modo demonstração"]');
    expect(faixa).not.toBeNull();
    expect(faixa?.textContent).toContain('nenhuma alteração é salva');
  });

  it('não aparece para as demais contas', () => {
    const elemento = montar(false);

    expect(elemento.querySelector('aside[aria-label="Modo demonstração"]')).toBeNull();
  });
});
