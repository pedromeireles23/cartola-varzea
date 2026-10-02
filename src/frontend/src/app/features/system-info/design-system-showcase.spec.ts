import { TestBed } from '@angular/core/testing';

import { DesignSystemShowcasePage } from './design-system-showcase';

describe('DesignSystemShowcasePage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [DesignSystemShowcasePage] });
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(DesignSystemShowcasePage);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('mantém os temas do jogador e do organizador isolados', async () => {
    const element = await render();
    const player = element.querySelector('[data-testid="theme-player"]');
    const organizer = element.querySelector('[data-testid="theme-organizer"]');

    expect(player).not.toBeNull();
    expect(organizer).not.toBeNull();
    expect(player?.textContent).toContain('Grafite e laranja');
    expect(organizer?.textContent).toContain('Revisão das súmulas');
  });

  it('exibe as quatro variantes de Card e a hierarquia até h3', async () => {
    const element = await render();
    const variants = [...element.querySelectorAll('app-card')].map((card) =>
      card.getAttribute('variant'),
    );

    expect(variants).toContain('default');
    expect(variants).toContain('elevated');
    expect(variants).toContain('highlight');
    expect(variants).toContain('compact');
    expect(element.querySelector('app-card[variant="elevated"] h3')).not.toBeNull();
  });

  it('demonstra PageHeader default, game e operational com níveis semânticos', async () => {
    const element = await render();

    expect(element.querySelector('[data-testid="header-default"] h1')).not.toBeNull();
    expect(element.querySelector('[data-testid="header-game"] h2')).not.toBeNull();
    expect(element.querySelector('[data-testid="header-operational"] h2')).not.toBeNull();
  });

  it('inclui tamanhos, estados, números esportivos e o painel operacional', async () => {
    const element = await render();

    expect(element.querySelector('app-button[size="regular"]')).not.toBeNull();
    expect(element.querySelector('app-button[size="compact"]')).not.toBeNull();
    expect(element.querySelector('app-badge[tone="info"]')).not.toBeNull();
    expect(element.querySelector('app-badge[tone="live"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="operational-panel"]')).not.toBeNull();
    expect(element.querySelector('.score.score--sm')).not.toBeNull();
    expect(element.querySelector('.position-number')).not.toBeNull();
    expect(element.querySelector('.round-clock')).not.toBeNull();
    expect(element.querySelectorAll('svg.lucide').length).toBeGreaterThan(0);
  });
});
