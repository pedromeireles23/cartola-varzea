import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { EmptyIllustration, EmptyState } from './empty-state';

@Component({
  imports: [EmptyState],
  template: `
    <app-empty-state [illustration]="ilustracao" heading="Nenhuma rodada ainda">
      Crie a primeira para marcar os jogos.
      <a emptyActions href="/rodadas">Criar rodada</a>
    </app-empty-state>
  `,
})
class Vitrine {
  ilustracao: EmptyIllustration = 'pitch';
}

describe('EmptyState', () => {
  async function abrir(ilustracao: EmptyIllustration = 'pitch'): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Vitrine);
    fixture.componentInstance.ilustracao = ilustracao;
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('diz o que falta em texto, com o título e a ação no lugar certo', async () => {
    const elemento = await abrir();

    expect(elemento.querySelector('.vazio__titulo')?.textContent).toBe('Nenhuma rodada ainda');
    expect(elemento.querySelector('.vazio__corpo')?.textContent).toContain(
      'Crie a primeira para marcar os jogos.',
    );
    expect(elemento.querySelector('.vazio__acoes a')?.getAttribute('href')).toBe('/rodadas');
  });

  it('o título não vira heading, para não quebrar a hierarquia da seção', async () => {
    const elemento = await abrir();

    expect(elemento.querySelector('h1, h2, h3, h4, h5, h6')).toBeNull();
  });

  it('cada desenho é ilustração, fora da árvore de acessibilidade', async () => {
    for (const ilustracao of ['pitch', 'tactics', 'stands', 'scoreboard'] as const) {
      const elemento = await abrir(ilustracao);
      const desenho = elemento.querySelector('svg');

      expect(desenho?.getAttribute('aria-hidden')).toBe('true');
      expect(desenho?.getAttribute('focusable')).toBe('false');
    }
  });
});
