import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { READ_ONLY_MODE } from '../../core/demo/read-only-mode';
import { Button } from './button';

@Component({
  imports: [Button],
  template: `
    <form (submit)="$event.preventDefault(); enviado = enviado + 1">
      <input aria-label="Nome" />
      <app-button escrita type="submit">Salvar</app-button>
    </form>
  `,
})
class Formulario {
  enviado = 0;
}

describe('Button', () => {
  it('bloqueia o clique durante o carregamento, sem esconder o rótulo', async () => {
    const fixture = TestBed.createComponent(Button);
    let cliques = 0;
    fixture.componentInstance.pressed.subscribe(() => cliques++);
    fixture.componentRef.setInput('loading', true);
    await fixture.whenStable();

    const botao = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    botao.click();

    expect(botao.disabled).toBe(true);
    expect(botao.getAttribute('aria-busy')).toBe('true');
    expect(cliques).toBe(0);
  });

  it('emite o clique quando habilitado', async () => {
    const fixture = TestBed.createComponent(Button);
    let cliques = 0;
    fixture.componentInstance.pressed.subscribe(() => cliques++);
    await fixture.whenStable();

    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();

    expect(cliques).toBe(1);
  });

  describe('na conta de demonstração', () => {
    const somenteLeitura = signal(true);

    beforeEach(() => {
      somenteLeitura.set(true);
      TestBed.configureTestingModule({
        providers: [{ provide: READ_ONLY_MODE, useValue: somenteLeitura }],
      });
    });

    it('a ação de escrita fica indisponível, focável e explicada', async () => {
      const fixture = TestBed.createComponent(Button);
      let cliques = 0;
      fixture.componentInstance.pressed.subscribe(() => cliques++);
      fixture.componentRef.setInput('escrita', true);
      await fixture.whenStable();

      const botao = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
      botao.click();

      expect(cliques).toBe(0);
      // aria-disabled, e não disabled: o botão continua na ordem do teclado.
      expect(botao.disabled).toBe(false);
      expect(botao.getAttribute('aria-disabled')).toBe('true');
      expect(botao.getAttribute('aria-describedby')).toBe('aviso-demo');
    });

    it('também não submete o formulário', async () => {
      const fixture = TestBed.createComponent(Formulario);
      await fixture.whenStable();

      (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();

      expect(fixture.componentInstance.enviado).toBe(0);
    });

    it('botão que não grava continua funcionando', async () => {
      const fixture = TestBed.createComponent(Button);
      let cliques = 0;
      fixture.componentInstance.pressed.subscribe(() => cliques++);
      await fixture.whenStable();

      (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();

      expect(cliques).toBe(1);
      expect(
        (fixture.nativeElement.querySelector('button') as HTMLButtonElement).hasAttribute(
          'aria-disabled',
        ),
      ).toBe(false);
    });

    it('fora da demonstração, a escrita volta a funcionar', async () => {
      somenteLeitura.set(false);
      const fixture = TestBed.createComponent(Button);
      let cliques = 0;
      fixture.componentInstance.pressed.subscribe(() => cliques++);
      fixture.componentRef.setInput('escrita', true);
      await fixture.whenStable();

      (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();

      expect(cliques).toBe(1);
    });
  });
});
