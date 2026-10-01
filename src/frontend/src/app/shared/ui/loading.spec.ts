import { TestBed } from '@angular/core/testing';

import { Loading, LoadingSkeleton } from './loading';

describe('Loading', () => {
  async function abrir(skeleton: LoadingSkeleton | null) {
    const fixture = TestBed.createComponent(Loading);
    fixture.componentRef.setInput('label', 'Abrindo o mercado…');
    fixture.componentRef.setInput('skeleton', skeleton);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('sem esqueleto, mostra o ponto e o rótulo, como antes', async () => {
    const elemento = await abrir(null);

    expect(elemento.querySelector('.loading__label')?.textContent).toBe('Abrindo o mercado…');
    expect(elemento.querySelector('.esqueleto')).toBeNull();
  });

  it('com esqueleto, a forma é decorativa e o rótulo continua para o leitor de tela', async () => {
    const elemento = await abrir('cards');
    const estado = elemento.querySelector('[role="status"]')!;

    expect(estado.getAttribute('aria-live')).toBe('polite');
    expect(estado.querySelector('.sr-only')?.textContent).toBe('Abrindo o mercado…');
    const formas = [...estado.querySelectorAll('.esqueleto__cartao')];
    expect(formas).toHaveLength(3);
    expect(formas.every((forma) => forma.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('cada formato desenha o que vem: linhas, cartões ou tabela', async () => {
    expect((await abrir('lines')).querySelectorAll('.esqueleto__linha')).toHaveLength(3);
    expect((await abrir('table')).querySelectorAll('.esqueleto__fileira')).toHaveLength(5);
  });
});
