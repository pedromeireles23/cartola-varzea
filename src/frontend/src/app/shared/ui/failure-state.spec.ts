import { TestBed } from '@angular/core/testing';

import { FailureState } from './failure-state';

describe('FailureState', () => {
  async function abrir(traceId?: string) {
    const fixture = TestBed.createComponent(FailureState);
    fixture.componentRef.setInput('failure', {
      status: 500,
      message: 'Não foi possível carregar agora.',
      traceId,
    });
    await fixture.whenStable();
    return fixture;
  }

  it('mostra a mensagem como erro e o código de rastreio quando houver', async () => {
    const fixture = await abrir('00-abc-01');
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.querySelector('[role="alert"]')?.textContent).toContain(
      'Não foi possível carregar agora.',
    );
    expect(elemento.textContent).toContain('Código de rastreio: 00-abc-01');
  });

  it('sem código de rastreio, não inventa um', async () => {
    const fixture = await abrir();

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Código de rastreio');
  });

  it('"Tentar de novo" avisa a tela, que decide como recarregar', async () => {
    const fixture = await abrir();
    let tentativas = 0;
    fixture.componentInstance.retry.subscribe(() => tentativas++);

    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();

    expect(tentativas).toBe(1);
  });
});
