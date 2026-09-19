import { ComponentFixture, TestBed } from '@angular/core/testing';

import { mercadoAberto } from './fantasy-fixtures';
import { MarketClock } from './market-clock';

describe('MarketClock', () => {
  const FECHA = Date.parse('2099-09-20T22:00:00Z');

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
  });

  afterEach(() => vi.useRealTimers());

  function abrir(): { fixture: ComponentFixture<MarketClock>; avisos: number[] } {
    const fixture = TestBed.createComponent(MarketClock);
    fixture.componentRef.setInput('market', mercadoAberto());
    const avisos: number[] = [];
    fixture.componentInstance.closed.subscribe(() => avisos.push(Date.now()));
    fixture.detectChanges();
    return { fixture, avisos };
  }

  it('só pergunta ao servidor depois do horário, mesmo com a contagem já zerada', () => {
    // Faltando meio segundo, a contagem já não mostra nada, mas o mercado não fechou.
    vi.setSystemTime(FECHA - 1_500);
    const { avisos } = abrir();

    vi.advanceTimersByTime(1_000);
    expect(avisos).toEqual([]);

    vi.advanceTimersByTime(1_000);
    expect(avisos).toEqual([FECHA + 500]);
  });

  it('se o servidor ainda disser "aberto", pergunta de novo em alguns segundos', () => {
    vi.setSystemTime(FECHA + 100);
    const { fixture, avisos } = abrir();

    vi.advanceTimersByTime(1_000);
    expect(avisos).toHaveLength(1);

    // A página recarregou e o servidor, um pouco atrasado, respondeu o mesmo mercado.
    fixture.componentRef.setInput('market', mercadoAberto());
    vi.advanceTimersByTime(3_000);
    expect(avisos).toHaveLength(1);
    vi.advanceTimersByTime(2_000);
    expect(avisos).toHaveLength(2);
  });

  it('com o mercado fechado, não pergunta nada', () => {
    vi.setSystemTime(FECHA + 100);
    const { fixture, avisos } = abrir();
    fixture.componentRef.setInput('market', { ...mercadoAberto(), isOpen: false });

    vi.advanceTimersByTime(10_000);
    expect(avisos).toEqual([]);
  });
});
