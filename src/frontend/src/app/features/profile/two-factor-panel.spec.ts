import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ApiFailure } from '../../core/api/problem-details';
import { TwoFactorService } from '../../core/auth/two-factor.service';
import { TwoFactorPanel } from './two-factor-panel';

const SETUP = {
  sharedKey: 'ABCD EFGH IJKL MNOP QRST UVWX YZ23 4567',
  authenticatorUri:
    'otpauth://totp/Cartola%20V%C3%A1rzea:admin%40exemplo.local?secret=ABCDEFGHIJKLMNOPQRSTUVWXYZ234567&issuer=Cartola%20V%C3%A1rzea&digits=6',
};

describe('TwoFactorPanel', () => {
  let servico: {
    status: ReturnType<typeof vi.fn>;
    beginAuthenticator: ReturnType<typeof vi.fn>;
    enable: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    servico = {
      status: vi.fn().mockResolvedValue({ enabled: false, recoveryCodesLeft: 0 }),
      beginAuthenticator: vi.fn().mockResolvedValue(SETUP),
      enable: vi.fn().mockResolvedValue(['B2C3D-F4G5H', 'J6K7M-N8P9Q']),
    };
    TestBed.configureTestingModule({
      imports: [TwoFactorPanel],
      providers: [{ provide: TwoFactorService, useValue: servico }],
    });
  });

  async function abrir(): Promise<ComponentFixture<TwoFactorPanel>> {
    const fixture = TestBed.createComponent(TwoFactorPanel);
    await fixture.whenStable();
    return fixture;
  }

  function elemento(fixture: ComponentFixture<TwoFactorPanel>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function botao(fixture: ComponentFixture<TwoFactorPanel>, texto: string): HTMLButtonElement {
    return [...elemento(fixture).querySelectorAll('button')].find((item) =>
      item.textContent?.includes(texto),
    )!;
  }

  async function configurar(fixture: ComponentFixture<TwoFactorPanel>): Promise<void> {
    botao(fixture, 'Ligar a verificação em duas etapas').click();
    await fixture.whenStable();
  }

  async function confirmar(
    fixture: ComponentFixture<TwoFactorPanel>,
    codigo: string,
  ): Promise<void> {
    const campo = elemento(fixture).querySelector<HTMLInputElement>(
      'input[autocomplete="one-time-code"]',
    )!;
    campo.value = codigo;
    campo.dispatchEvent(new Event('input'));
    elemento(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('desligada, explica por que a administração pede e oferece ligar', async () => {
    const fixture = await abrir();

    expect(elemento(fixture).textContent).toContain('aplicativo autenticador');
    expect(botao(fixture, 'Ligar a verificação em duas etapas')).toBeDefined();
  });

  it('ao ligar, mostra o QR code, a chave e o atalho para o aplicativo', async () => {
    const fixture = await abrir();
    await configurar(fixture);

    const qr = elemento(fixture).querySelector('svg[role="img"]');
    expect(qr?.getAttribute('aria-label')).toBe('QR code para o aplicativo autenticador');
    expect(qr?.querySelector('path')?.getAttribute('d')).toMatch(/^M\d+ \d+h1v1h-1z/);
    expect(elemento(fixture).querySelector('.chave')?.textContent).toBe(SETUP.sharedKey);
    expect(elemento(fixture).querySelector('a')?.getAttribute('href')).toBe(SETUP.authenticatorUri);
  });

  it('o código certo liga e mostra os códigos de recuperação uma vez', async () => {
    const fixture = await abrir();
    await configurar(fixture);
    await confirmar(fixture, '123456');

    expect(servico.enable).toHaveBeenCalledWith('123456');
    const codigos = [...elemento(fixture).querySelectorAll('.codigos li')].map((item) =>
      item.textContent?.trim(),
    );
    expect(codigos).toEqual(['B2C3D-F4G5H', 'J6K7M-N8P9Q']);

    servico.status.mockResolvedValue({ enabled: true, recoveryCodesLeft: 2 });
    botao(fixture, 'Já guardei os códigos').click();
    await fixture.whenStable();

    expect(elemento(fixture).textContent).toContain('Ligada');
    expect(elemento(fixture).textContent).toContain('Restam 2 códigos de recuperação');
    expect(elemento(fixture).querySelector('.codigos')).toBeNull();
  });

  it('código errado fica no passo, com o motivo', async () => {
    servico.enable.mockRejectedValue({ status: 400, message: 'x' } satisfies ApiFailure);
    const fixture = await abrir();
    await configurar(fixture);
    await confirmar(fixture, '000000');

    expect(elemento(fixture).textContent).toContain('O código não confere');
    expect(elemento(fixture).querySelector('svg[role="img"]')).not.toBeNull();
  });
});
