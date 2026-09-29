import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { Badge } from './badge';
import { Button } from './button';
import { Card } from './card';
import { Panel } from './panel';
import { PageHeader } from './page-header';

@Component({
  imports: [Badge, Button, Card, PageHeader, Panel],
  template: `
    <section class="theme-player">
      <app-page-header
        heading="Rodada 8"
        kicker="Mercado aberto"
        variant="game"
        [headingLevel]="2"
      />
      <app-card heading="Sua pontuação" variant="highlight" [headingLevel]="3">
        <span class="score">78,4</span>
      </app-card>
      <app-panel heading="Confrontos" variant="operational" [headingLevel]="3">
        <span panel-actions data-testid="panel-actions">Ao vivo</span>
        <span data-testid="panel-body">Vila Matilde × Jardim Pery</span>
        <span panel-footer data-testid="panel-footer">Atualizado agora</span>
      </app-panel>
      <app-button size="compact">Escalar</app-button>
      <app-badge tone="live">Em andamento</app-badge>
    </section>
  `,
})
class VisualVariantsHost {}

describe('Variantes da fundação visual', () => {
  it('expõe variantes tipadas sem perder a hierarquia semântica', async () => {
    await TestBed.configureTestingModule({ imports: [VisualVariantsHost] }).compileComponents();
    const fixture = TestBed.createComponent(VisualVariantsHost);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-page-header')?.classList.contains('page-header--game')).toBe(
      true,
    );
    expect(element.querySelector('app-page-header h2')?.textContent).toContain('Rodada 8');
    expect(element.querySelector('.card')?.classList.contains('card--highlight')).toBe(true);
    expect(element.querySelector('.card h3')?.textContent).toContain('Sua pontuação');
    expect(element.querySelector('.painel')?.classList.contains('painel--operacional')).toBe(true);
    expect(element.querySelector('.painel h3')?.textContent).toContain('Confrontos');
    expect(element.querySelector('.painel__topo [data-testid="panel-actions"]')).not.toBeNull();
    expect(element.querySelector('.painel__corpo [data-testid="panel-body"]')).not.toBeNull();
    expect(element.querySelector('.painel__rodape [data-testid="panel-footer"]')).not.toBeNull();
    expect(element.querySelector('.painel__corpo [panel-actions]')).toBeNull();
    expect(element.querySelector('.painel__corpo [panel-footer]')).toBeNull();
    expect(element.querySelector('.btn')?.classList.contains('btn--compact')).toBe(true);
    expect(element.querySelector('.badge')?.classList.contains('badge--live')).toBe(true);
  });
});
