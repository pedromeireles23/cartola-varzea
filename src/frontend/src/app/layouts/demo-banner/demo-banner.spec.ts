import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { PublicLayout } from '../public-layout/public-layout';

describe('Faixa de modo demonstração', () => {
  function montar(demo: boolean): HTMLElement {
    TestBed.configureTestingModule({
      imports: [PublicLayout],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { current: signal(null), isDemoViewer: signal(demo) },
        },
      ],
    });
    const fixture = TestBed.createComponent(PublicLayout);
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
