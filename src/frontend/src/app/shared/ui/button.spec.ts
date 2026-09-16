import { TestBed } from '@angular/core/testing';

import { Button } from './button';

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
});
